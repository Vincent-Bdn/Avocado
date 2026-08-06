using System.Text.Json.Serialization;

namespace Avocado.Vault.Keys;

public enum VaultKeyKind
{
    /// <summary>OS-backed, bound to one user account on one machine. The everyday path.</summary>
    Device,

    /// <summary>A printed or USB-stored 256-bit key. The disaster path.</summary>
    Recovery,

    /// <summary>Opt-in, off by default.</summary>
    Passphrase,
}

/// <summary>
/// One way to get at the data encryption key. Each entry holds the same DEK wrapped by a different key
/// encryption key, so adding or removing an unlock path never re-encrypts a single byte of vault data —
/// and neither does changing a passphrase.
/// </summary>
public sealed record VaultKeyEntry
{
    public required Guid Id { get; init; }

    public required VaultKeyKind Kind { get; init; }

    /// <summary>Shown in the UI, e.g. "Windows (LAPTOP-ANNE\anne)".</summary>
    public required string Label { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Salt for the KDF. Empty for <see cref="VaultKeyKind.Device"/>.</summary>
    public required byte[] Salt { get; init; }

    /// <summary>The DEK sealed under this entry's key encryption key.</summary>
    public required byte[] WrappedDataKey { get; init; }

    /// <summary>Argon2id cost parameters. Only for <see cref="VaultKeyKind.Passphrase"/>.</summary>
    public Crypto.Argon2Parameters? Kdf { get; init; }

    /// <summary>The KEK, protected by the OS. Only for <see cref="VaultKeyKind.Device"/>.</summary>
    public byte[]? ProtectedKeyEncryptionKey { get; init; }

    /// <summary>
    /// The recovery key itself, sealed under the data encryption key. Only for
    /// <see cref="VaultKeyKind.Recovery"/>, and absent on vaults created before this existed.
    /// <para>
    /// It costs nothing: reading it requires the DEK, and anyone holding the DEK can already read the
    /// whole practice. What it buys is real, though. The quarterly check can ask for two groups out of
    /// nine and actually verify them, and a lost sheet can be reprinted without invalidating every
    /// backup taken so far by issuing a new key.
    /// </para>
    /// </summary>
    public byte[]? SealedRecoveryKey { get; init; }
}

public sealed record VaultKeyringDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public required Guid VaultId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required List<VaultKeyEntry> Keys { get; init; }
}

/// <summary>What the UI needs to render the keyring, without the key material.</summary>
public sealed record VaultKeyInfo(Guid Id, VaultKeyKind Kind, string Label, DateTimeOffset CreatedAt);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(VaultKeyringDocument))]
internal sealed partial class VaultKeyringJsonContext : JsonSerializerContext;
