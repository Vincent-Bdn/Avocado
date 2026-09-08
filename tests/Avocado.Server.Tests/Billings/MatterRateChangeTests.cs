using Avocado.Server.Features.Matters;
using Avocado.Server.Features.Matters.Endpoints;
using Avocado.Server.Features.Matters.Endpoints.Dtos;
using Avocado.Server.Features.TimeEntries;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests.Billings;

/// <summary>
/// Changing a dossier's taux horaire, which used to be silently ignored.
///
/// <para>Every assertion here is about money that has already been earned, and in several cases
/// already invoiced. The rule is that changing the rate from today must not alter a single centime
/// of what is behind it.</para>
/// </summary>
public sealed class MatterRateChangeTests : IDisposable
{
    private readonly TestVault _vault = new();

    private static MatterInput Rate(Matter matter, long? cents) => new(
        Name: matter.Name,
        ClientContactId: Guid.CreateVersion7(),
        Reference: matter.Reference,
        Description: null,
        OpenedOn: matter.OpenedOn,
        HourlyRateCents: cents,
        CourtCaseNumber: null);

    [Fact]
    public async Task WritesTheNewRate()
    {
        var matter = Dossier(20_000);

        await UpdateMatter.HandleAsync(matter.Id, Rate(matter, 24_000), _vault.Database, default);

        Assert.Equal(24_000, (await Reload(matter)).HourlyRateCents);
    }

    /// <summary>
    /// The reason the edit was refused for so long, and the reason it can be allowed now.
    /// </summary>
    [Fact]
    public async Task LeavesEveryHourAlreadyRecordedWorthWhatItWasWorth()
    {
        var matter = Dossier(20_000);
        var entry = Hours(matter, minutes: 120, overrideCents: null);

        var before = entry.AmountCents(matter.HourlyRateCents);
        Assert.Equal(40_000, before);   // 2 h at 200 €.

        await UpdateMatter.HandleAsync(matter.Id, Rate(matter, 24_000), _vault.Database, default);

        var reloaded = await Reload(matter);
        var after = await _vault.Database.TimeEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);

        // Pinned to the old rate rather than left to follow the dossier's.
        Assert.Equal(20_000, after.HourlyRateCentsOverride);
        Assert.Equal(before, after.AmountCents(reloaded.HourlyRateCents));
    }

    [Fact]
    public async Task LeavesADerogationAlone()
    {
        var matter = Dossier(20_000);
        var entry = Hours(matter, minutes: 60, overrideCents: 15_000);

        await UpdateMatter.HandleAsync(matter.Id, Rate(matter, 24_000), _vault.Database, default);

        var after = await _vault.Database.TimeEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);

        // It was never following the dossier's rate, so there was nothing to pin.
        Assert.Equal(15_000, after.HourlyRateCentsOverride);
    }

    [Fact]
    public async Task ChargesTheNewRateForWhatComesAfterwards()
    {
        var matter = Dossier(20_000);
        Hours(matter, minutes: 60, overrideCents: null);

        await UpdateMatter.HandleAsync(matter.Id, Rate(matter, 24_000), _vault.Database, default);

        var reloaded = await Reload(matter);
        var tomorrow = Hours(reloaded, minutes: 60, overrideCents: null);

        Assert.Equal(24_000, tomorrow.AmountCents(reloaded.HourlyRateCents));
    }

    [Fact]
    public async Task DoesNotTouchAnotherDossiersHours()
    {
        var mine = Dossier(20_000);
        var other = Dossier(30_000, "2026-0002");
        var elsewhere = Hours(other, minutes: 60, overrideCents: null);

        await UpdateMatter.HandleAsync(mine.Id, Rate(mine, 24_000), _vault.Database, default);

        var after = await _vault.Database.TimeEntries.AsNoTracking().SingleAsync(e => e.Id == elsewhere.Id);
        Assert.Null(after.HourlyRateCentsOverride);
    }

    [Fact]
    public async Task PinsNothingWhenTheRateHasNotMoved()
    {
        var matter = Dossier(20_000);
        var entry = Hours(matter, minutes: 60, overrideCents: null);

        // Saving the fiche without touching the rate is the common case, and it must not quietly
        // stamp a dérogation onto every line of the dossier.
        await UpdateMatter.HandleAsync(matter.Id, Rate(matter, 20_000), _vault.Database, default);

        var after = await _vault.Database.TimeEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        Assert.Null(after.HourlyRateCentsOverride);
    }

    [Fact]
    public async Task IgnoresARateThatWasNotSent()
    {
        var matter = Dossier(20_000);
        var entry = Hours(matter, minutes: 60, overrideCents: null);

        await UpdateMatter.HandleAsync(matter.Id, Rate(matter, null), _vault.Database, default);

        Assert.Equal(20_000, (await Reload(matter)).HourlyRateCents);
        Assert.Null((await _vault.Database.TimeEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id))
            .HourlyRateCentsOverride);
    }

    private Matter Dossier(long rateCents, string reference = "2026-0001") => _vault.Save(new Matter
    {
        Reference = reference,
        Name = "Durand contre Martin",
        OpenedOn = new DateOnly(2026, 1, 5),
        HourlyRateCents = rateCents,
    });

    private TimeEntry Hours(Matter matter, int minutes, long? overrideCents) => _vault.Save(new TimeEntry
    {
        MatterId = matter.Id,
        Date = new DateOnly(2026, 2, 3),
        DurationMinutes = minutes,
        Task = "Rédaction des conclusions",
        HourlyRateCentsOverride = overrideCents,
    });

    private async Task<Matter> Reload(Matter matter)
    {
        _vault.Database.ChangeTracker.Clear();
        return await _vault.Database.Matters.AsNoTracking().SingleAsync(m => m.Id == matter.Id);
    }

    public void Dispose() => _vault.Dispose();
}
