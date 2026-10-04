using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Billings.Endpoints;
using Avocado.Server.Features.Billings.ValueObjects;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.TimeEntries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// The Documentation screen's worked example against a real dossier holding the same thing.
///
/// <para>A page that explains the figures is only worth having if it cannot be wrong, and the way it
/// would go wrong is the obvious one: the explanation drifts while the application moves. These
/// build each scenario twice, once as rows in a database and once as the posted simulation, and
/// require the two summaries to be equal in every field. If the model changes under either, this
/// goes red before a lawyer reads something untrue.</para>
/// </summary>
public class SimulatedBillingTests : IDisposable
{
    private readonly TestVault _vault = new();

    private static BillingSummary Simulated(BillingScenario scenario) =>
        Assert.IsType<Ok<BillingSummary>>(SimulateBilling.Handle(scenario)).Value!;

    /// <summary>The same scenario, as a dossier with its time, its facture and its mouvements.</summary>
    private async Task<BillingSummary> RealAsync(BillingScenario scenario)
    {
        var matter = _vault.Save(new Matter
        {
            Reference = "700770",
            Name = "COULEYRE / EDF ENR",
            OpenedOn = new DateOnly(2026, 1, 6),
            HourlyRateCents = scenario.HourlyRateCents,
            FixedFeeCents = scenario.FixedFeeCents,
        });

        TimeEntry Entry(int minutes) => _vault.Save(new TimeEntry
        {
            MatterId = matter.Id,
            Date = new DateOnly(2026, 8, 27),
            DurationMinutes = minutes,
            Task = "Diligences",
            IsBillable = true,
        });

        if (scenario.UnbilledMinutes > 0) Entry(scenario.UnbilledMinutes);

        if (scenario.BilledMinutes > 0)
        {
            await BillTimeEntries.HandleAsync(
                matter.Id,
                new BillTimeInput(
                    [Entry(scenario.BilledMinutes).Id],
                    new DateOnly(2026, 9, 1),
                    scenario.InvoicedCents,
                    ExternalReference: "F-2026-001",
                    IsPaid: scenario.IsPaid),
                _vault.Database,
                default);
        }

        if (scenario.AbandonedMinutes > 0)
        {
            await BillTimeEntries.HandleAsync(
                matter.Id,
                new BillTimeInput(
                    [Entry(scenario.AbandonedMinutes).Id],
                    new DateOnly(2026, 9, 2),
                    AmountExclVatCents: null,
                    ExternalReference: null,
                    IsAbandon: true),
                _vault.Database,
                default);
        }

        foreach (var amount in new[] { scenario.ProvisionCents, -scenario.DisbursementCents })
        {
            if (amount == 0) continue;

            _vault.Save(new BillingLedgerEntry
            {
                MatterId = matter.Id,
                Date = new DateOnly(2026, 8, 1),
                AmountCents = amount,
                Label = "Mouvement",
            });
        }

        if (scenario.SubcontractedCents > 0)
        {
            _vault.Save(new BillingCost
            {
                MatterId = matter.Id,
                Date = new DateOnly(2026, 8, 10),
                Label = "Rétrocession",
                AmountExclVatCents = scenario.SubcontractedCents,
            });
        }

        return await BillingSummaryQuery.ForMatterAsync(_vault.Database, matter.Id, default);
    }

    public static TheoryData<string, BillingScenario> Scenarios() => new()
    {
        {
            "à l'heure, rien de facturé",
            new BillingScenario(24_000, null, 12 * 60, 0, null, false, 0, 0, 0, 0)
        },
        {
            "une facture au prix des heures",
            new BillingScenario(24_000, null, 3 * 60, 12 * 60, null, true, 0, 0, 0, 0)
        },
        {
            // « 7 200 € d'heures facturées 6 000 € » : le mali qu'elle consent.
            "un mali consenti",
            new BillingScenario(24_000, null, 3 * 60, 30 * 60, 600_000, false, 0, 0, 0, 0)
        },
        {
            "un boni",
            new BillingScenario(24_000, null, 0, 10 * 60, 300_000, true, 0, 0, 0, 0)
        },
        {
            // Le forfait décide, les heures mesurent l'écart.
            "un forfait à moitié facturé",
            new BillingScenario(24_000, 300_000, 5 * 60, 8 * 60, 100_000, false, 0, 0, 0, 0)
        },
        {
            "un forfait dépassé",
            new BillingScenario(24_000, 300_000, 4 * 60, 20 * 60, 300_000, true, 0, 0, 0, 0)
        },
        {
            // Une provision est de l'argent reçu, pas du travail facturé.
            "une provision reçue d'avance",
            new BillingScenario(24_000, null, 10 * 60, 0, null, false, 0, 150_000, 0, 0)
        },
        {
            "un débours à refacturer",
            new BillingScenario(24_000, null, 6 * 60, 0, null, false, 0, 0, 42_000, 0)
        },
        {
            "du temps soldé en mali",
            new BillingScenario(24_000, null, 2 * 60, 6 * 60, 120_000, true, 5 * 60, 0, 0, 0)
        },
        {
            "une rétrocession au confrère",
            new BillingScenario(24_000, null, 0, 20 * 60, 500_000, true, 0, 0, 0, 180_000)
        },
        {
            "tout à la fois",
            new BillingScenario(24_000, 400_000, 4 * 60, 15 * 60, 250_000, true, 2 * 60, 100_000, 35_000, 60_000)
        },
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task SaysExactlyWhatTheFicheWouldSay(string _, BillingScenario scenario)
    {
        // Records compare by value, so one assertion covers every figure including the ones a future
        // change might add.
        Assert.Equal(await RealAsync(scenario), Simulated(scenario));
    }

    /// <summary>A scenario with nothing in it answers zeroes rather than failing.</summary>
    [Fact]
    public void HandlesAnEmptyScenario()
    {
        var summary = Simulated(new BillingScenario(24_000, null, 0, 0, null, false, 0, 0, 0, 0));

        Assert.Equal(0, summary.WorkedCents);
        Assert.Equal(0, summary.LeftToBillCents);
        Assert.Equal(0, summary.VarianceCents);
    }

    [Fact]
    public void RefusesNegativeDurations()
    {
        var result = SimulateBilling.Handle(
            new BillingScenario(24_000, null, -60, 0, null, false, 0, 0, 0, 0));

        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _vault.Dispose();
    }
}
