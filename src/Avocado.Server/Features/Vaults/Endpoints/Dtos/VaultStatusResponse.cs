using Avocado.Server.Features.Vaults.Enums;

namespace Avocado.Server.Features.Vaults.Endpoints.Dtos;

/// <param name="SuggestedDirectory">
/// Where the wizard should point by default. Already checked against the cloud-sync detector, so the
/// suggestion is never one the app would then refuse.
/// </param>
public sealed record VaultStatusResponse(
    VaultState State,
    string Directory,
    string? LockReason,
    Guid? VaultId,
    bool HasRecoveryKey,
    string SuggestedDirectory);

/// <param name="AllowSyncedFolder">
/// The « passer outre » escape hatch. Available because the detector is a heuristic, and quiet
/// because using it is how databases get corrupted.
/// </param>
public sealed record VaultCreateRequest(string Directory, bool AllowSyncedFolder = false);

/// <param name="RecoveryCode">
/// Returned once and never again. Nine groups of six; the wizard must not let the user continue until
/// it has been printed or written to removable media. At this point nothing has been written to disk —
/// these are keys held in memory, and abandoning the wizard abandons them.
/// </param>
public sealed record VaultPreparedResponse(string RecoveryCode);

public sealed record VaultCreatedResponse(Guid VaultId, string Directory);

public sealed record VaultUnlockRequest(string RecoveryCode);
