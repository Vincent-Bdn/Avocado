using Avocado.Server.Features.Billings.ValueObjects;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <param name="UnbilledMinutes">Time recorded and not yet taken by anything.</param>
/// <param name="BilledMinutes">Time the facture below took with it.</param>
/// <param name="InvoicedCents">What she actually billed for those hours. Null bills them at value.</param>
/// <param name="AbandonedMinutes">Time soldé en mali: taken, and billed nothing.</param>
public sealed record BillingScenario(
    long HourlyRateCents,
    long? FixedFeeCents,
    int UnbilledMinutes,
    int BilledMinutes,
    long? InvoicedCents,
    bool IsPaid,
    int AbandonedMinutes,
    long ProvisionCents,
    long DisbursementCents,
    long SubcontractedCents);

/// <summary>
/// The Documentation screen's worked example, computed by the code that computes her own fiche.
///
/// <para>Nothing here decides anything: it turns « 12 heures à 240 € » into the rows a dossier would
/// hold and hands them to <see cref="BillingSummary.From"/>. That is the whole point. A page that
/// explains the figures by reproducing their arithmetic in TypeScript would be the fourth
/// implementation of « reste à facturer », and the first three disagreed; this one cannot, because
/// it is not an implementation.</para>
///
/// <para>Nothing is read and nothing is written. It needs no dossier and touches no vault.</para>
/// </summary>
public static class SimulateBilling
{
    private const int MaxMinutes = 100_000 * 60;
    private const long MaxCents = 100_000_000_00;

    public static IResult Handle(BillingScenario scenario)
    {
        if (scenario.HourlyRateCents is < 0 or > MaxCents
            || scenario.UnbilledMinutes is < 0 or > MaxMinutes
            || scenario.BilledMinutes is < 0 or > MaxMinutes
            || scenario.AbandonedMinutes is < 0 or > MaxMinutes)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["scenario"] = ["Indiquez des durées et un taux positifs."],
            });
        }

        var rate = scenario.HourlyRateCents;

        List<BillingTimeLine> time =
        [
            new(scenario.UnbilledMinutes, null, IsBilled: false),
            new(scenario.BilledMinutes, null, IsBilled: true),
            new(scenario.AbandonedMinutes, null, IsBilled: true),
        ];

        var billedTimeCents = rate * scenario.BilledMinutes / 60;
        var abandonedTimeCents = rate * scenario.AbandonedMinutes / 60;

        List<BillingFacture> factures = [];

        if (scenario.BilledMinutes > 0)
        {
            factures.Add(new BillingFacture(
                scenario.InvoicedCents ?? billedTimeCents,
                billedTimeCents,
                IsAbandon: false,
                scenario.IsPaid,
                DeductsWithoutLines: false));
        }

        if (scenario.AbandonedMinutes > 0)
        {
            factures.Add(new BillingFacture(
                0,
                abandonedTimeCents,
                IsAbandon: true,
                IsPaid: false,
                DeductsWithoutLines: false));
        }

        List<long> ledger = [];

        if (scenario.ProvisionCents > 0) ledger.Add(scenario.ProvisionCents);
        if (scenario.DisbursementCents > 0) ledger.Add(-scenario.DisbursementCents);

        return Results.Ok(BillingSummary.From(new BillingInputs(
            rate,
            scenario.FixedFeeCents,
            time,
            factures,
            ledger,
            scenario.SubcontractedCents)));
    }
}
