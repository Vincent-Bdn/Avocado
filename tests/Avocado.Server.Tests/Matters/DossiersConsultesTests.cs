using Avocado.Server.Features.Activities;
using Avocado.Server.Features.Activities.Enums;
using Avocado.Server.Features.Dashboards;
using Avocado.Server.Features.Dashboards.Endpoints;
using Avocado.Server.Features.Dashboards.ValueObjects;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.Matters.Endpoints;
using Avocado.Server.Tests.Dashboards;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Tests.Matters;

/// <summary>
/// « Dossiers consultés », which used to be « dossiers récemment touchés ».
///
/// <para>The two are not the same list, which is why a lawyer asked. Touched was the latest of five
/// timestamps of work recorded against a dossier, so importing a mailbox floated every dossier in the
/// cabinet to the top at once and the list stopped meaning anything. Consulted is the fiches she has
/// opened.</para>
/// </summary>
public class DossiersConsultesTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly FakeTimeProvider _clock = new(new DateTime(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc));

    private Matter Matter(string name) =>
        _vault.Save(new Matter
        {
            Reference = name,
            Name = name,
            OpenedOn = new DateOnly(2026, 1, 6),
            HourlyRateCents = 24_000,
        });

    /// <summary>Work recorded against a dossier, which is what « touché » meant.</summary>
    private void Worked(Matter matter, DateTimeOffset at) =>
        _vault.Save(new Activity
        {
            MatterId = matter.Id,
            Type = ActivityType.Note,
            OccurredAt = at,
            Body = "Diligences",
        });

    private Task Open(Matter matter) =>
        GetMatter.HandleAsync(matter.Id, _vault.Database, _clock, default);

    private async Task<IReadOnlyList<string>> ConsultedAsync()
    {
        var result = await GetDashboard.HandleAsync(_vault.Database, _clock, default);

        return [.. Assert.IsType<Ok<DashboardSummary>>(result).Value!.RecentMatters.Select(m => m.Name)];
    }

    /// <summary>
    /// Opening a fiche is the event, and nothing else is. The dossier she worked on last week and
    /// opened this morning comes first, above the one an import touched a minute ago.
    /// </summary>
    [Fact]
    public async Task OrdersByTheFichesSheOpened()
    {
        var untouched = Matter("JAMAIS OUVERT");
        var consulted = Matter("CONSULTÉ CE MATIN");

        Worked(consulted, _clock.GetUtcNow().AddDays(-7));
        await Open(consulted);

        // And then an import touches the other one, an hour after she closed the first.
        _clock.Advance(TimeSpan.FromHours(1));
        Worked(untouched, _clock.GetUtcNow());

        // Later, and still second: she has never opened it.
        Assert.Equal(["CONSULTÉ CE MATIN", "JAMAIS OUVERT"], await ConsultedAsync());
    }

    /// <summary>
    /// A dossier nobody has opened since this existed still appears, ordered by the last work on it.
    /// On the day this ships that is every dossier, and an empty accueil would read as a bug.
    /// </summary>
    [Fact]
    public async Task FallsBackToTheLastWorkOnADossierNeverOpened()
    {
        var older = Matter("ANCIEN");
        var newer = Matter("RÉCENT");

        Worked(older, _clock.GetUtcNow().AddDays(-9));
        Worked(newer, _clock.GetUtcNow().AddDays(-2));

        Assert.Equal(["RÉCENT", "ANCIEN"], await ConsultedAsync());
    }

    /// <summary>A dossier with no work and no view has no place in the list.</summary>
    [Fact]
    public async Task LeavesOutADossierNothingHasHappenedTo()
    {
        Matter("JAMAIS OUVERT");

        Assert.Empty(await ConsultedAsync());
    }

    /// <summary>
    /// The fiche refetches whenever anything on it changes, so recording every read would write a row
    /// per keystroke in the journal. Within the minute, the first view stands.
    /// </summary>
    [Fact]
    public async Task DoesNotRewriteTheViewOnEveryRefetch()
    {
        var matter = Matter("DOSSIER");

        await Open(matter);
        var first = await ViewedAtAsync(matter);

        _clock.Advance(TimeSpan.FromSeconds(20));
        await Open(matter);

        Assert.Equal(first, await ViewedAtAsync(matter));

        // Past the minute it moves again, so « consultés » keeps up with an afternoon of work.
        _clock.Advance(TimeSpan.FromMinutes(2));
        await Open(matter);

        Assert.True(await ViewedAtAsync(matter) > first);
    }

    /// <summary>Consulting a dossier is not changing it, so « modifié le » must not move.</summary>
    [Fact]
    public async Task LeavesUpdatedAtAlone()
    {
        var matter = Matter("DOSSIER");
        var before = matter.UpdatedAt;

        _clock.Advance(TimeSpan.FromHours(3));
        await Open(matter);

        var stored = await _vault.Database.Matters.AsNoTracking()
            .FirstAsync(candidate => candidate.Id == matter.Id);

        Assert.Equal(before, stored.UpdatedAt);
        Assert.NotNull(stored.LastViewedAt);
    }

    private async Task<DateTimeOffset?> ViewedAtAsync(Matter matter) =>
        (await _vault.Database.Matters.AsNoTracking()
            .FirstAsync(candidate => candidate.Id == matter.Id)).LastViewedAt;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _vault.Dispose();
    }
}
