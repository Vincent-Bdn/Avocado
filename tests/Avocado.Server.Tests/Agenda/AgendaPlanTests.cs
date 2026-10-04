using Avocado.Server.Features.Agenda;
using Avocado.Server.Features.Agenda.ValueObjects;
using Avocado.Server.Features.Deadlines.Enums;

namespace Avocado.Server.Tests.Agenda;

/// <summary>
/// What gets sent to Google, decided without a Google account.
///
/// <para>Everything that can put a lawyer in the wrong place on the wrong day is here: an échéance
/// that never arrived, one that moved and did not follow, one deleted in the dossier and left
/// standing in her agenda. The part that talks to Google only carries this list out.</para>
/// </summary>
public class AgendaPlanTests
{
    private static AgendaEntry Entry(
        Guid id,
        string label = "Audience de plaidoirie",
        string matter = "COULEYRE / EDF ENR",
        int day = 12,
        TimeOnly? time = null,
        int remind = 2,
        bool done = false) =>
        new(id, label, DeadlineType.Hearing, "700770", matter, new DateOnly(2026, 11, day), time, remind, done);

    private static AgendaEvent Pushed(AgendaEntry entry, string eventId = "evt-1") =>
        new() { DeadlineId = entry.DeadlineId, GoogleEventId = eventId, Fingerprint = entry.Fingerprint };

    /// <summary>What Avocado still holds. Anything absent here has genuinely been deleted.</summary>
    private static HashSet<Guid> Live(params AgendaEntry[] entries) =>
        [.. entries.Select(entry => entry.DeadlineId)];

    [Fact]
    public void InsertsAnEcheanceNobodyHasSentYet()
    {
        var entry = Entry(Guid.CreateVersion7());

        var action = Assert.Single(AgendaPlan.For([entry], [], Live(entry)));

        Assert.Equal(AgendaActionKind.Insert, action.Kind);
        Assert.Equal(entry.DeadlineId, action.DeadlineId);
        Assert.Null(action.EventId);
    }

    /// <summary>
    /// A pass runs every quarter of an hour. Resending a few hundred unchanged événements each time
    /// would be rate-limited by Google within the hour and would stamp « modifié » on all of them.
    /// </summary>
    [Fact]
    public void SendsNothingWhenNothingChanged()
    {
        var entry = Entry(Guid.CreateVersion7());

        Assert.Empty(AgendaPlan.For([entry], [Pushed(entry)], Live(entry)));
    }

    /// <summary>
    /// Every field she can edit has to reach the agenda. A date that moved and did not follow is the
    /// defect this whole feature exists to avoid.
    /// </summary>
    [Theory]
    [InlineData("date")]
    [InlineData("heure")]
    [InlineData("intitulé")]
    [InlineData("dossier")]
    [InlineData("rappel")]
    [InlineData("faite")]
    public void UpdatesWhateverSheChanged(string what)
    {
        var id = Guid.CreateVersion7();
        var before = Entry(id, time: new TimeOnly(9, 0));

        var after = what switch
        {
            "date" => before with { Date = before.Date.AddDays(3) },
            "heure" => before with { Time = new TimeOnly(14, 30) },
            "intitulé" => before with { Label = "Audience de mise en état" },
            "dossier" => before with { MatterName = "COULEYRE / ENGIE" },
            "rappel" => before with { RemindDaysBefore = 7 },
            _ => before with { IsDone = true },
        };

        var action = Assert.Single(AgendaPlan.For([after], [Pushed(before)], Live(after)));

        Assert.Equal(AgendaActionKind.Update, action.Kind);
        Assert.Equal("evt-1", action.EventId);
        Assert.Equal(after.Fingerprint, action.Entry!.Fingerprint);
    }

    /// <summary>
    /// Deleted in the dossier, deleted in the agenda. Leaving it would put her in a tribunal on a
    /// date nobody expects her, which is worse than never having pushed it at all.
    /// </summary>
    [Fact]
    public void RemovesWhatAvocadoNoLongerHas()
    {
        var gone = Entry(Guid.CreateVersion7());

        var action = Assert.Single(AgendaPlan.For([], [Pushed(gone, "evt-9")], Live()));

        Assert.Equal(AgendaActionKind.Delete, action.Kind);
        Assert.Equal("evt-9", action.EventId);
        Assert.Null(action.Entry);
    }

    [Fact]
    public void HandlesAllThreeAtOnce()
    {
        var unchanged = Entry(Guid.CreateVersion7());
        var moved = Entry(Guid.CreateVersion7(), day: 3);
        var fresh = Entry(Guid.CreateVersion7(), day: 20);
        var removed = Entry(Guid.CreateVersion7(), day: 28);

        var actions = AgendaPlan.For(
            [unchanged, moved with { Date = new DateOnly(2026, 11, 4) }, fresh],
            [Pushed(unchanged, "a"), Pushed(moved, "b"), Pushed(removed, "c")],
            Live(unchanged, moved, fresh));

        Assert.Equal(
            [AgendaActionKind.Update, AgendaActionKind.Insert, AgendaActionKind.Delete],
            actions.Select(action => action.Kind));
    }

    /// <summary>
    /// The title is the only part a calendar shows in a list, and « Conclusions » is useless when
    /// four dossiers have one that month.
    /// </summary>
    [Fact]
    public void PutsTheDossierInTheTitle()
    {
        var entry = Entry(Guid.CreateVersion7(), label: "Conclusions");

        Assert.Equal("Conclusions · COULEYRE / EDF ENR", entry.Summary);
        Assert.Contains("700770", entry.Description);
        Assert.Contains("Avocado", entry.Description);
    }

    /// <summary>
    /// Two échéances that read identically on screen are still two events. They differ by nothing a
    /// person can see, so the fingerprint must not be what distinguishes them: the key is.
    /// </summary>
    [Fact]
    public void TreatsTwoIdenticalEcheancesAsTwoEvents()
    {
        var first = Entry(Guid.CreateVersion7());
        var second = Entry(Guid.CreateVersion7());

        Assert.Equal(first.Fingerprint, second.Fingerprint);

        var actions = AgendaPlan.For([first, second], [Pushed(first)], Live(first, second));

        var action = Assert.Single(actions);
        Assert.Equal(AgendaActionKind.Insert, action.Kind);
        Assert.Equal(second.DeadlineId, action.DeadlineId);
    }
}
