namespace Avocado.Server.Features.Settings.Endpoints.Dtos;

/// <param name="HourlyRateCents">
/// What a new dossier starts from. Never read again afterwards: the rate is copied onto the dossier
/// at creation, so changing this figure prices tomorrow's work and leaves yesterday's alone.
/// </param>
/// <param name="EmailAddresses">
/// Hers, one per line. What tells a courriel reçu from a courriel envoyé.
/// </param>
public sealed record PracticeSettings(
    long HourlyRateCents,
    IReadOnlyList<string>? EmailAddresses = null);

/// <param name="VaultDirectory">
/// Where the coffre is: the journal, the tiers, the facturation, the temps passé and the modèles.
/// Not the documents, which live in her own folders, one per dossier.
/// </param>
public sealed record PracticeInfo(
    long HourlyRateCents,
    IReadOnlyList<string> EmailAddresses,
    string VaultDirectory);
