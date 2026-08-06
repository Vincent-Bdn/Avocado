namespace Avocado.Server.Features.Vaults.Enums;

public enum VaultState
{
    /// <summary>No vault in the configured folder. First run — the setup wizard has to happen.</summary>
    Absent,

    /// <summary>
    /// A vault is there but this machine cannot open it: a restored folder, a new computer, a
    /// different Windows account. The recovery key is the way in.
    /// </summary>
    Locked,

    Unlocked,
}
