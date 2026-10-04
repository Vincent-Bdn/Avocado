namespace Avocado.Server.Features.Billings.ValueObjects;

/// <param name="DurationMinutes">Billable only. Time marked non facturable never reaches here.</param>
/// <param name="RateCentsOverride">« Je ne facture que la moitié », agreed on that line alone.</param>
/// <param name="IsBilled">Taken by a facture or an abandon, and therefore no longer waiting.</param>
public readonly record struct BillingTimeLine(
    int DurationMinutes,
    long? RateCentsOverride,
    bool IsBilled);

/// <param name="BilledTimeCents">
/// What the lines it took were worth, frozen on the day it was issued. Zero when it took none, which
/// is a reprise or a facture typed in by hand.
/// </param>
public readonly record struct BillingFacture(
    long AmountExclVatCents,
    long BilledTimeCents,
    bool IsAbandon,
    bool IsPaid,
    bool DeductsWithoutLines);

/// <summary>
/// Everything one dossier's figures are computed from, as plain rows.
///
/// <para>It exists so that <see cref="BillingSummary.From"/> is reachable without a database. The
/// arithmetic went wrong twice, both times in which rows were fed to it rather than in the sums, and
/// that decision used to live inside a LINQ query no test could reach without building a whole
/// dossier. It is also what the Documentation screen's simulator posts, so the worked examples a
/// lawyer reads cannot drift away from what her own fiche does.</para>
/// </summary>
/// <param name="LedgerCents">
/// Signed, as stored: received from the client is positive, advanced on his behalf negative.
/// </param>
public sealed record BillingInputs(
    long HourlyRateCents,
    long? FixedFeeCents,
    IReadOnlyList<BillingTimeLine> Time,
    IReadOnlyList<BillingFacture> Factures,
    IReadOnlyList<long> LedgerCents,
    long SubcontractedCents);
