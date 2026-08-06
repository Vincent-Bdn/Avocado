namespace Avocado.Server.Features.Settings.Endpoints.Dtos;

/// <param name="HourlyRateCents">
/// What a new dossier starts from. Never read again afterwards: the rate is copied onto the dossier
/// at creation, so changing this figure prices tomorrow's work and leaves yesterday's alone.
/// </param>
public sealed record PracticeSettings(long HourlyRateCents);
