namespace Avocado.Vault.Storage;

/// <summary>
/// A vault is a folder. Configure a folder and that is the whole install — backup is copying it,
/// moving machines is moving it.
/// </summary>
public sealed class VaultPaths
{
    public VaultPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        Root = Path.GetFullPath(rootDirectory);
    }

    public string Root { get; }

    /// <summary>Salt, KDF parameters and the list of wrapped data keys.</summary>
    public string KeyringFile => Path.Combine(Root, "vault.json");

    /// <summary>SQLCipher database holding every structured record.</summary>
    public string DatabaseFile => Path.Combine(Root, "avocado.db");

    /// <summary>Encrypted document blobs, content-addressed.</summary>
    public string BlobsDirectory => Path.Combine(Root, "blobs");

    /// <summary>Rolling snapshots, including the automatic pre-migration copy.</summary>
    public string BackupsDirectory => Path.Combine(Root, "backups");

    /// <summary>True if this folder already holds a vault.</summary>
    public bool Exists => File.Exists(KeyringFile);

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BlobsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
