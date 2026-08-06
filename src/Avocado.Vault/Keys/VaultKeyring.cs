using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Keys;

/// <summary>
/// The envelope. One random 256-bit data encryption key encrypts everything in the vault and never
/// changes; this class manages the list of ways to unwrap it.
/// <para>
/// Because the DEK is constant, enrolling a new unlock path, revoking one, or changing the passphrase
/// are all O(1) — they rewrite <c>vault.json</c> and nothing else. It is also what will let a second
/// user be added later without re-encrypting the practice.
/// </para>
/// </summary>
public sealed class VaultKeyring
{
    private const string DataKeyInfo = "avocado-dek-v1";
    private const string RecoveryKekInfo = "avocado-recovery-kek-v1";
    private const int SaltSize = 16;

    private readonly string _path;
    private VaultKeyringDocument _document;

    private VaultKeyring(string path, VaultKeyringDocument document)
    {
        _path = path;
        _document = document;
    }

    public Guid VaultId => _document.VaultId;

    public IReadOnlyList<VaultKeyInfo> Keys =>
        [.. _document.Keys.Select(k => new VaultKeyInfo(k.Id, k.Kind, k.Label, k.CreatedAt))];

    public bool HasRecoveryKey => _document.Keys.Any(k => k.Kind == VaultKeyKind.Recovery);

    public bool HasPassphrase => _document.Keys.Any(k => k.Kind == VaultKeyKind.Passphrase);

    public bool HasDeviceKey => _document.Keys.Any(k => k.Kind == VaultKeyKind.Device);

    /// <summary>
    /// Creates a brand new keyring: a fresh DEK, a device entry if the platform supports one, and a
    /// recovery key. The recovery code is returned exactly once and never recoverable afterwards —
    /// the caller is responsible for making the user save it before continuing.
    /// </summary>
    public static VaultKeyringCreation Create(string path, IDeviceKeyStore deviceKeyStore)
    {
        var creation = Prepare(path, deviceKeyStore);
        creation.Keyring.Persist();
        return creation;
    }

    /// <summary>
    /// Builds the same keyring <em>without touching the disk</em>. The setup wizard shows the recovery
    /// code before anything is written, so that going back leaves nothing behind to clean up — and a
    /// folder is only created once the whole flow has been seen through.
    /// </summary>
    public static VaultKeyringCreation Prepare(string path, IDeviceKeyStore deviceKeyStore)
    {
        if (File.Exists(path))
        {
            throw new VaultException($"A keyring already exists at '{path}'.");
        }

        var document = new VaultKeyringDocument
        {
            VaultId = Guid.CreateVersion7(),
            CreatedAt = DateTimeOffset.UtcNow,
            Keys = [],
        };

        var keyring = new VaultKeyring(path, document);
        var dataKey = SecretKey.Generate();

        try
        {
            if (deviceKeyStore.IsSupported)
            {
                keyring.EnrollDeviceKey(dataKey, deviceKeyStore, save: false);
            }

            var recoveryCode = keyring.EnrollRecoveryKey(dataKey, save: false);
            return new VaultKeyringCreation(keyring, dataKey, recoveryCode);
        }
        catch
        {
            dataKey.Dispose();
            throw;
        }
    }

    public static VaultKeyring Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new VaultCorruptedException($"No keyring found at '{path}'. Without it the vault cannot be opened.");
        }

        VaultKeyringDocument? document;
        try
        {
            using var stream = File.OpenRead(path);
            document = JsonSerializer.Deserialize(stream, VaultKeyringJsonContext.Default.VaultKeyringDocument);
        }
        catch (JsonException ex)
        {
            throw new VaultCorruptedException($"The keyring at '{path}' is not readable.", ex);
        }

        if (document is null)
        {
            throw new VaultCorruptedException($"The keyring at '{path}' is empty.");
        }

        if (document.Version > VaultKeyringDocument.CurrentVersion)
        {
            throw new VaultCorruptedException(
                $"This vault was created by a newer version of Avocado (keyring version {document.Version}). Upgrade to open it.");
        }

        if (document.Keys.Count == 0)
        {
            throw new VaultCorruptedException(
                $"The keyring at '{path}' contains no keys, so the vault can never be unlocked.");
        }

        return new VaultKeyring(path, document);
    }

    /// <summary>Unlocks using the OS key store. Tries every device entry — a vault may legitimately
    /// have been enrolled on more than one machine.</summary>
    public SecretKey UnlockWithDeviceKey(IDeviceKeyStore deviceKeyStore)
    {
        if (!deviceKeyStore.IsSupported)
        {
            throw new DeviceKeyStoreUnavailableException(
                "No OS-backed key store on this platform. Unlock with a passphrase or the recovery key.");
        }

        var candidates = _document.Keys.Where(k => k.Kind == VaultKeyKind.Device).ToList();
        if (candidates.Count == 0)
        {
            throw new VaultUnlockFailedException("This vault has no device key enrolled on any machine.");
        }

        foreach (var entry in candidates)
        {
            if (entry.ProtectedKeyEncryptionKey is null)
            {
                continue;
            }

            byte[] rawKek;
            try
            {
                rawKek = deviceKeyStore.Unprotect(entry.ProtectedKeyEncryptionKey);
            }
            catch (VaultException)
            {
                continue;  // Enrolled on a different account or machine.
            }

            try
            {
                using var kek = new SecretKey(rawKek);
                return Unwrap(entry, kek);
            }
            catch (CryptographicException)
            {
                continue;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rawKek);
            }
        }

        throw new VaultUnlockFailedException(
            "This vault's device key belongs to a different Windows account or machine. Use the recovery key.");
    }

    public SecretKey UnlockWithRecoveryCode(string recoveryCode)
    {
        if (!RecoveryCode.TryParse(recoveryCode, out var recoveryKey) || recoveryKey is null)
        {
            throw new VaultUnlockFailedException(
                "That recovery key isn't valid. Check for a mistyped character — the code has a built-in checksum.");
        }

        using (recoveryKey)
        {
            var entry = _document.Keys.FirstOrDefault(k => k.Kind == VaultKeyKind.Recovery)
                ?? throw new VaultUnlockFailedException("This vault has no recovery key enrolled.");

            using var kek = KeyDerivation.Hkdf(recoveryKey.Span, entry.Salt, RecoveryKekInfo);
            try
            {
                return Unwrap(entry, kek);
            }
            catch (CryptographicException ex)
            {
                throw new VaultUnlockFailedException("That recovery key does not open this vault.", ex);
            }
        }
    }

    public SecretKey UnlockWithPassphrase(string passphrase)
    {
        var entry = _document.Keys.FirstOrDefault(k => k.Kind == VaultKeyKind.Passphrase)
            ?? throw new VaultUnlockFailedException("This vault has no passphrase set.");

        using var kek = KeyDerivation.Argon2id(passphrase, entry.Salt, entry.Kdf ?? Argon2Parameters.Default);
        try
        {
            return Unwrap(entry, kek);
        }
        catch (CryptographicException ex)
        {
            throw new VaultUnlockFailedException("Incorrect passphrase.", ex);
        }
    }

    /// <summary>
    /// Issues a fresh recovery key, replacing any existing one. Available whenever the vault can still
    /// be opened, which is what narrows total data loss to "machine dead AND recovery key lost".
    /// </summary>
    public string RegenerateRecoveryKey(SecretKey dataKey) => EnrollRecoveryKey(dataKey, save: true);

    public void EnrollDeviceKey(SecretKey dataKey, IDeviceKeyStore deviceKeyStore) =>
        EnrollDeviceKey(dataKey, deviceKeyStore, save: true);

    public void SetPassphrase(SecretKey dataKey, string passphrase, Argon2Parameters? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        var kdf = parameters ?? Argon2Parameters.Default;
        var id = Guid.CreateVersion7();
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        using var kek = KeyDerivation.Argon2id(passphrase, salt, kdf);

        Replace(VaultKeyKind.Passphrase, new VaultKeyEntry
        {
            Id = id,
            Kind = VaultKeyKind.Passphrase,
            Label = "Passphrase",
            CreatedAt = DateTimeOffset.UtcNow,
            Salt = salt,
            Kdf = kdf,
            WrappedDataKey = Aead.Seal(kek, dataKey.Span, AssociatedData(_document.VaultId, id)),
        });

        Save();
    }

    /// <summary>Revokes an unlock path — a retired laptop, a recovery sheet that was left on a train.</summary>
    public void Remove(Guid keyId)
    {
        var entry = _document.Keys.FirstOrDefault(k => k.Id == keyId)
            ?? throw new VaultException($"No key '{keyId}' in this keyring.");

        if (_document.Keys.Count == 1)
        {
            throw new VaultException(
                "This is the only way left to open the vault. Enrol another unlock method before removing it.");
        }

        _document = _document with { Keys = [.. _document.Keys.Where(k => k.Id != entry.Id)] };
        Save();
    }

    private void EnrollDeviceKey(SecretKey dataKey, IDeviceKeyStore deviceKeyStore, bool save)
    {
        if (!deviceKeyStore.IsSupported)
        {
            throw new DeviceKeyStoreUnavailableException(
                "No OS-backed key store on this platform, so the vault cannot open without a passphrase here.");
        }

        var id = Guid.CreateVersion7();
        using var kek = SecretKey.Generate();

        // The DEK itself is never handed to the OS API — only this intermediate KEK is.
        var protectedKek = deviceKeyStore.Protect(kek.Span);

        var entry = new VaultKeyEntry
        {
            Id = id,
            Kind = VaultKeyKind.Device,
            Label = deviceKeyStore.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            Salt = [],
            WrappedDataKey = Aead.Seal(kek, dataKey.Span, AssociatedData(_document.VaultId, id)),
            ProtectedKeyEncryptionKey = protectedKek,
        };

        // Re-enrolling on the same machine replaces that machine's entry rather than piling up.
        var existing = _document.Keys.FirstOrDefault(
            k => k.Kind == VaultKeyKind.Device && k.Label == entry.Label);

        _document = _document with
        {
            Keys = [.. _document.Keys.Where(k => k.Id != existing?.Id), entry],
        };

        if (save)
        {
            Save();
        }
    }

    private string EnrollRecoveryKey(SecretKey dataKey, bool save)
    {
        var id = Guid.CreateVersion7();
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        using var recoveryKey = SecretKey.Generate();
        using var kek = KeyDerivation.Hkdf(recoveryKey.Span, salt, RecoveryKekInfo);

        Replace(VaultKeyKind.Recovery, new VaultKeyEntry
        {
            Id = id,
            Kind = VaultKeyKind.Recovery,
            Label = "Recovery key",
            CreatedAt = DateTimeOffset.UtcNow,
            Salt = salt,
            WrappedDataKey = Aead.Seal(kek, dataKey.Span, AssociatedData(_document.VaultId, id)),
        });

        if (save)
        {
            Save();
        }

        return RecoveryCode.Format(recoveryKey);
    }

    private void Replace(VaultKeyKind kind, VaultKeyEntry entry) =>
        _document = _document with { Keys = [.. _document.Keys.Where(k => k.Kind != kind), entry] };

    private SecretKey Unwrap(VaultKeyEntry entry, SecretKey kek)
    {
        var raw = Aead.Open(kek, entry.WrappedDataKey, AssociatedData(_document.VaultId, entry.Id));
        try
        {
            return new SecretKey(raw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    /// <summary>
    /// Binds each wrapping to its vault and its entry, so a wrapped key cannot be lifted from one
    /// keyring and pasted into another to make it decrypt something it was never meant to.
    /// </summary>
    private static byte[] AssociatedData(Guid vaultId, Guid keyId)
    {
        var prefix = Encoding.UTF8.GetBytes(DataKeyInfo);
        var data = new byte[prefix.Length + 32];
        prefix.CopyTo(data.AsSpan());
        vaultId.TryWriteBytes(data.AsSpan(prefix.Length, 16));
        keyId.TryWriteBytes(data.AsSpan(prefix.Length + 16, 16));
        return data;
    }

    /// <summary>Writes a keyring built by <see cref="Prepare"/> to disk for the first time.</summary>
    public void Persist() => Save();

    /// <summary>
    /// Written via a temp file and an atomic move. Losing <c>vault.json</c> to a half-completed write
    /// would lose the vault, so this must never leave a truncated file behind.
    /// </summary>
    private void Save()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = _path + ".tmp";

        using (var stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, _document, VaultKeyringJsonContext.Default.VaultKeyringDocument);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, _path, overwrite: true);
    }
}

/// <param name="RecoveryCode">
/// Shown to the user exactly once. It cannot be recovered from the keyring afterwards — only replaced.
/// </param>
public sealed record VaultKeyringCreation(VaultKeyring Keyring, SecretKey DataKey, string RecoveryCode);
