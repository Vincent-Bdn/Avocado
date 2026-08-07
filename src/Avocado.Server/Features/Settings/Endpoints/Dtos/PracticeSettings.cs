namespace Avocado.Server.Features.Settings.Endpoints.Dtos;

/// <param name="HourlyRateCents">
/// What a new dossier starts from. Never read again afterwards: the rate is copied onto the dossier
/// at creation, so changing this figure prices tomorrow's work and leaves yesterday's alone.
/// </param>
public sealed record PracticeSettings(long HourlyRateCents);

/// <param name="WorkingDirectory">
/// Where documents are decrypted to while they are open in Word. Read-only: it is derived from the
/// platform's per-user application-state folder, and it is shown rather than configured because a
/// machine-local scratch folder is not a decision anyone has a basis to make.
/// </param>
public sealed record PracticeInfo(
    long HourlyRateCents,
    string VaultDirectory,
    string WorkingDirectory);
