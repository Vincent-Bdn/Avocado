namespace Avocado.Vault;

public class VaultException : Exception
{
    public VaultException(string message) : base(message) { }
    public VaultException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The supplied protector could not unwrap the data encryption key.</summary>
public sealed class VaultUnlockFailedException : VaultException
{
    public VaultUnlockFailedException(string message) : base(message) { }
    public VaultUnlockFailedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Vault metadata is missing, unreadable, or internally inconsistent.</summary>
public sealed class VaultCorruptedException : VaultException
{
    public VaultCorruptedException(string message) : base(message) { }
    public VaultCorruptedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The chosen folder is inside a cloud-sync root. Its own type, not just a message, because the UI
/// branches on it to offer the override, and matching on the text of an English exception from a
/// French interface is a bug waiting to happen.
/// </summary>
public sealed class SyncedFolderException : VaultException
{
    public SyncedFolderException(string message, string detectedRoot) : base(message) =>
        DetectedRoot = detectedRoot;

    public string DetectedRoot { get; }
}

/// <summary>
/// A backup destination was asked to do something while it was not connected. Its own type because
/// the backup service treats it as "try again in a minute" rather than as an error worth telling
/// anyone about: a USB key spends most of its life unplugged, and that is not a fault.
/// </summary>
public sealed class SinkUnavailableException : VaultException
{
    public SinkUnavailableException(string message) : base(message) { }
}

/// <summary>This platform has no OS-backed key store, so the device unlock path is unavailable.</summary>
public sealed class DeviceKeyStoreUnavailableException : VaultException
{
    public DeviceKeyStoreUnavailableException(string message) : base(message) { }
}
