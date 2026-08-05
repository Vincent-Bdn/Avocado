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

/// <summary>This platform has no OS-backed key store, so the device unlock path is unavailable.</summary>
public sealed class DeviceKeyStoreUnavailableException : VaultException
{
    public DeviceKeyStoreUnavailableException(string message) : base(message) { }
}
