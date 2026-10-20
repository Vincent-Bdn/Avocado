using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Activities.Enums;
using Avocado.Server.Features.Billings;
using Avocado.Server.Features.Billings.Enums;
using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Deadlines;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.Matters.Endpoints;
using Avocado.Server.Features.TimeEntries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests.Matters;

/// <summary>
/// Fusionner deux dossiers qui sont le même.
///
/// <para>« Problème avec dossier FINANCIERE CN NIETO / FINANCIERE NIETO, 2 dossiers. Peut-on merger
/// sur FINANCIERE CN (NIETO) ? »</para>
///
/// <para>It cannot be undone and it moves money, so every row that can be lost has a test. The one
/// that corrupts silently is the taux horaire: a time entry with no rate of its own is valued at its
/// dossier's rate, so moving it to a dossier at a different rate reprices history retroactively,
/// with nothing on screen to say so.</para>
/// </summary>
public sealed class MergeMattersTests : IDisposable
{
    private readonly TestVault _vault = new();

    private Matter Dossier(string name, long rateCents = 20_000, string? folder = null) =>
        _vault.Save(new Matter
        {
            Reference = $"2026-{Math.Abs(name.GetHashCode(StringComparison.Ordinal)) % 9000 + 1000}",
            Name = name,
            OpenedOn = new DateOnly(2026, 1, 5),
            HourlyRateCents = rateCents,
            DocumentsFolder = folder,
        });

    private Contact Tiers(string legalName) => _vault.Save(new Contact
    {
        Type = ContactType.Organisation,
        LegalName = legalName,
    });

    private MatterParty Partie(Matter matter, Contact contact, bool client, string? role = null) =>
        _vault.Save(new MatterParty
        {
            MatterId = matter.Id,
            ContactId = contact.Id,
            IsClient = client,
            Role = role,
        });

    private TimeEntry Heures(Matter matter, int minutes, long? rateOverride = null) =>
        _vault.Save(new TimeEntry
        {
            MatterId = matter.Id,
            Date = new DateOnly(2026, 3, 10),
            DurationMinutes = minutes,
            Task = "Travail",
            IsBillable = true,
            HourlyRateCentsOverride = rateOverride,
        });

    private async Task<IResult> Merge(Matter source, Matter target, string? folder = null) =>
        await MergeMatters.HandleAsync(
            source.Id, target.Id, new MergeMatterInput(folder), _vault.Database, default);

    private async Task<MergePreview> Preview(Matter source, Matter target)
    {
        var result = await MergeMatters.PreviewAsync(source.Id, target.Id, _vault.Database, default);

        return Assert.IsType<MergePreview>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
    }

    // ─── The one that corrupts money ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A line relying on its dossier's rate keeps that rate when it moves. The dossier rate is
    /// snapshotted per dossier precisely so history cannot be repriced, and a merge must not be the
    /// hole in that.
    /// </summary>
    [Fact]
    public async Task FreezesTheRateOnTheLinesItMoves()
    {
        var source = Dossier("FINANCIERE NIETO", rateCents: 15_000);
        var target = Dossier("FINANCIERE CN (NIETO)", rateCents: 30_000);

        var relying = Heures(source, 120);
        var itsOwn = Heures(source, 60, rateOverride: 10_000);

        await Merge(source, target);

        var moved = await _vault.Database.TimeEntries.AsNoTracking()
            .FirstAsync(entry => entry.Id == relying.Id);

        // Two hours were worth 300 € on the dossier at 150 €/h, and they still are — valued here with
        // the target's 300 €/h, which is exactly the rate the frozen override has to beat.
        Assert.Equal(15_000, moved.HourlyRateCentsOverride);
        Assert.Equal(30_000, moved.AmountCents(matterHourlyRateCents: 30_000));
        Assert.Equal(target.Id, moved.MatterId);

        // A line that already carried its own rate is left exactly as it was.
        Assert.Equal(
            10_000,
            (await _vault.Database.TimeEntries.AsNoTracking().FirstAsync(entry => entry.Id == itsOwn.Id))
                .HourlyRateCentsOverride);
    }

    /// <summary>At the same rate there is nothing to freeze, and nothing is written.</summary>
    [Fact]
    public async Task LeavesTheRateAloneWhenTheTwoDossiersAgree()
    {
        var source = Dossier("FINANCIERE NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)");
        var entry = Heures(source, 120);

        await Merge(source, target);

        Assert.Null((await _vault.Database.TimeEntries.AsNoTracking()
            .FirstAsync(row => row.Id == entry.Id)).HourlyRateCentsOverride);
    }

    // ─── Nothing is lost ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MovesEverythingThatHangsOffTheDossier()
    {
        var source = Dossier("FINANCIERE NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)");

        _vault.Save(new Activity
        {
            MatterId = source.Id,
            Type = ActivityType.Call,
            OccurredAt = DateTimeOffset.UtcNow,
            Body = "Appel du client",
        });

        _vault.Save(new Deadline
        {
            MatterId = source.Id,
            Date = new DateOnly(2026, 6, 1),
            Label = "Conclusions",
        });

        Heures(source, 180);

        _vault.Save(new BillingInvoice
        {
            MatterId = source.Id,
            Date = new DateOnly(2026, 4, 1),
            AmountExclVatCents = 120_000,
        });

        _vault.Save(new BillingLedgerEntry
        {
            MatterId = source.Id,
            Date = new DateOnly(2026, 4, 2),
            AmountCents = 50_000,
            Label = "Provision",
        });

        _vault.Save(new BillingCost
        {
            MatterId = source.Id,
            Date = new DateOnly(2026, 4, 3),
            AmountExclVatCents = 20_000,
            Label = "Rétrocession",
            Kind = "Rétrocession d'honoraires",
        });

        var result = await Merge(source, target);
        Assert.IsAssignableFrom<IValueHttpResult>(result);

        Assert.Equal(1, await _vault.Database.Activities.CountAsync(row => row.MatterId == target.Id));
        Assert.Equal(1, await _vault.Database.Deadlines.CountAsync(row => row.MatterId == target.Id));
        Assert.Equal(1, await _vault.Database.TimeEntries.CountAsync(row => row.MatterId == target.Id));
        Assert.Equal(1, await _vault.Database.Invoices.CountAsync(row => row.MatterId == target.Id));
        Assert.Equal(1, await _vault.Database.LedgerEntries.CountAsync(row => row.MatterId == target.Id));
        Assert.Equal(1, await _vault.Database.Costs.CountAsync(row => row.MatterId == target.Id));

        // And the dossier she was merging away is gone, rather than left in her lists holding nothing.
        Assert.Null(await _vault.Database.Matters.FindAsync(source.Id));
    }

    /// <summary>
    /// A facture keeps the lines it took. The foreign key is on the entry, so moving both sides keeps
    /// the boni and the mali exactly as they were decided.
    /// </summary>
    [Fact]
    public async Task KeepsAFactureAttachedToItsLines()
    {
        var source = Dossier("FINANCIERE NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)");

        var invoice = _vault.Save(new BillingInvoice
        {
            MatterId = source.Id,
            Date = new DateOnly(2026, 4, 1),
            AmountExclVatCents = 50_000,
            BilledTimeCents = 60_000,
        });

        var entry = Heures(source, 180);
        entry.InvoiceId = invoice.Id;
        _vault.Database.SaveChanges();

        await Merge(source, target);

        var moved = await _vault.Database.TimeEntries.AsNoTracking().FirstAsync(row => row.Id == entry.Id);

        Assert.Equal(invoice.Id, moved.InvoiceId);
        Assert.Equal(target.Id, moved.MatterId);
        Assert.Equal(
            -10_000,
            (await _vault.Database.Invoices.AsNoTracking().FirstAsync(row => row.Id == invoice.Id))
                .VarianceCents);
    }

    // ─── Parties ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NeverListsTheSameTiersTwice()
    {
        var client = Tiers("FINANCIERE CN");
        var adverse = Tiers("Partie adverse");

        var source = Dossier("FINANCIERE NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)");

        Partie(source, client, client: true);
        Partie(source, adverse, client: false, role: "Partie adverse");
        Partie(target, client, client: true, role: "Client");

        await Merge(source, target);

        var parties = await _vault.Database.MatterParties.AsNoTracking()
            .Where(party => party.MatterId == target.Id)
            .ToListAsync();

        Assert.Equal(2, parties.Count);
        Assert.Single(parties, party => party.ContactId == client.Id);

        // The target's own row is the one that survives, role and all.
        Assert.Equal("Client", parties.First(party => party.ContactId == client.Id).Role);
    }

    /// <summary>
    /// A dossier whose client is nobody cannot be billed on, so when the target has no client and the
    /// source's duplicate row was one, the surviving row is promoted.
    /// </summary>
    [Fact]
    public async Task PromotesTheClientWhenTheTargetHasNone()
    {
        var contact = Tiers("FINANCIERE CN");
        var source = Dossier("FINANCIERE NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)");

        Partie(source, contact, client: true);
        Partie(target, contact, client: false, role: "À préciser");

        await Merge(source, target);

        var party = Assert.Single(await _vault.Database.MatterParties.AsNoTracking()
            .Where(row => row.MatterId == target.Id)
            .ToListAsync());

        Assert.True(party.IsClient);
    }

    // ─── The documents folder ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdoptsTheSourceFolderWhenTheTargetHasNone()
    {
        var source = Dossier("FINANCIERE NIETO", folder: @"C:\Dossiers\NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)");

        await Merge(source, target);

        Assert.Equal(
            @"C:\Dossiers\NIETO",
            (await _vault.Database.Matters.AsNoTracking().FirstAsync(row => row.Id == target.Id))
                .DocumentsFolder);
    }

    [Fact]
    public async Task KeepsTheTargetFolderByDefault()
    {
        var source = Dossier("FINANCIERE NIETO", folder: @"C:\Dossiers\NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)", folder: @"C:\Dossiers\CN");

        await Merge(source, target);

        Assert.Equal(
            @"C:\Dossiers\CN",
            (await _vault.Database.Matters.AsNoTracking().FirstAsync(row => row.Id == target.Id))
                .DocumentsFolder);
    }

    [Fact]
    public async Task LetsHerKeepTheOtherFolderInstead()
    {
        var source = Dossier("FINANCIERE NIETO", folder: @"C:\Dossiers\NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)", folder: @"C:\Dossiers\CN");

        await Merge(source, target, folder: @"C:\Dossiers\NIETO");

        Assert.Equal(
            @"C:\Dossiers\NIETO",
            (await _vault.Database.Matters.AsNoTracking().FirstAsync(row => row.Id == target.Id))
                .DocumentsFolder);
    }

    /// <summary>Pointing a merged dossier somewhere neither of them was is not a merge.</summary>
    [Fact]
    public async Task RefusesAThirdFolder()
    {
        var source = Dossier("FINANCIERE NIETO", folder: @"C:\Dossiers\NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)", folder: @"C:\Dossiers\CN");

        var result = await Merge(source, target, folder: @"C:\Ailleurs");

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.NotNull(await _vault.Database.Matters.FindAsync(source.Id));
    }

    // ─── Refusals and the preview ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A double-click on the confirm button would otherwise merge a dossier into itself, which means
    /// deleting it with everything still attached.
    /// </summary>
    [Fact]
    public async Task RefusesToMergeADossierWithItself()
    {
        var matter = Dossier("FINANCIERE CN (NIETO)");
        Heures(matter, 60);

        var result = await MergeMatters.HandleAsync(
            matter.Id, matter.Id, new MergeMatterInput(), _vault.Database, default);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.NotNull(await _vault.Database.Matters.FindAsync(matter.Id));
        Assert.Equal(1, await _vault.Database.TimeEntries.CountAsync());
    }

    [Fact]
    public async Task CountsWhatWillMoveAndSaysWhatItIsWorriedAbout()
    {
        var source = Dossier("FINANCIERE NIETO", rateCents: 15_000, folder: @"C:\Dossiers\NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)", rateCents: 30_000, folder: @"C:\Dossiers\CN");

        Heures(source, 180);
        _vault.Save(new BillingInvoice
        {
            MatterId = source.Id,
            Date = new DateOnly(2026, 4, 1),
            AmountExclVatCents = 120_000,
        });

        var shared = Tiers("FINANCIERE CN");
        Partie(source, shared, client: true);
        Partie(target, shared, client: true);

        var preview = await Preview(source, target);

        Assert.Equal(1, preview.TimeEntries);
        Assert.Equal(180, preview.BillableMinutes);
        Assert.Equal(1, preview.Invoices);
        Assert.Equal(120_000, preview.InvoicedCents);
        Assert.Equal(1, preview.Parties);
        Assert.Equal(1, preview.PartiesAlreadyThere);

        Assert.Contains(preview.Warnings, warning => warning.Contains("taux horaire"));
        Assert.Contains(preview.Warnings, warning => warning.Contains("répertoires différents"));

        // And it has recorded nothing.
        Assert.NotNull(await _vault.Database.Matters.FindAsync(source.Id));
    }

    /// <summary>An abandon is not turnover, so the preview does not count it as money moving.</summary>
    [Fact]
    public async Task LeavesAbandonsOutOfThePreviewTotal()
    {
        var source = Dossier("FINANCIERE NIETO");
        var target = Dossier("FINANCIERE CN (NIETO)");

        _vault.Save(new BillingInvoice
        {
            MatterId = source.Id,
            Date = new DateOnly(2026, 4, 1),
            AmountExclVatCents = 0,
            BilledTimeCents = 90_000,
            IsAbandon = true,
        });

        var preview = await Preview(source, target);

        Assert.Equal(1, preview.Invoices);
        Assert.Equal(0, preview.InvoicedCents);
    }

    public void Dispose() => _vault.Dispose();
}
