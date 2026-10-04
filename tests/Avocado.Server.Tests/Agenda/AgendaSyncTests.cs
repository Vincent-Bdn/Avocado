using Avocado.Server.Features.Agenda;
using Avocado.Server.Features.Agenda.Infrastructure;
using Avocado.Server.Features.Agenda.ValueObjects;
using Avocado.Server.Features.Deadlines;
using Avocado.Server.Features.Deadlines.Enums;
using Avocado.Server.Features.Matters;
using Avocado.Server.Features.Settings;
using Avocado.Server.Tests.Dashboards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avocado.Server.Tests.Agenda;

/// <summary>
/// A pass, against a calendar that records what it was told rather than a Google account.
///
/// <para>What the plan decides is pinned next door. This is the half around it: that a closed dossier
/// stops producing events without erasing the ones it has, that an event she deleted in Google comes
/// back, that a failure is counted and eventually gives up, and that disconnecting forgets enough to
/// be able to reconnect.</para>
/// </summary>
public class AgendaSyncTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly FakeCalendar _calendar = new();
    private readonly FakeTimeProvider _clock = new(new DateTime(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc));
    private readonly AgendaConnection _connection;
    private readonly AgendaSync _sync;
    private readonly Matter _matter;

    public AgendaSyncTests()
    {
        _connection = new AgendaConnection(
            new GoogleOAuth(new FakeHttpClientFactory()),
            new ConfigurationBuilder().Build(),
            _clock);

        _sync = new AgendaSync(_connection, _calendar, _clock, NullLogger<AgendaSync>.Instance);

        _matter = _vault.Save(new Matter
        {
            Reference = "700770",
            Name = "COULEYRE / EDF ENR",
            OpenedOn = new DateOnly(2026, 1, 6),
            HourlyRateCents = 24_000,
        });

        // Connected, without going through Google: the flow is not what these are about.
        Set(AgendaSettingKeys.RefreshToken, "refresh");
        Set(AgendaSettingKeys.CalendarId, FakeCalendar.Id);
    }

    private void Set(string key, string value) =>
        _vault.Save(new PracticeSetting { Key = key, Value = value });

    private Deadline Deadline(string label = "Audience", int day = 12, bool done = false) =>
        _vault.Save(new Deadline
        {
            MatterId = _matter.Id,
            Date = new DateOnly(2026, 11, day),
            Type = DeadlineType.Hearing,
            Label = label,
            RemindDaysBefore = 2,
            IsDone = done,
        });

    private Task<AgendaSyncResult> SyncAsync(bool force = false) =>
        _sync.RunAsync(_vault.Database, force, default);

    [Fact]
    public async Task PutsEveryEcheanceInTheCalendarOnce()
    {
        Deadline("Audience", 12);
        Deadline("Conclusions", 20);

        Assert.Equal(2, (await SyncAsync()).Sent);
        Assert.Equal(2, _calendar.Events.Count);

        // And a second pass sends nothing, which is what stops Google rate-limiting her.
        Assert.Equal(0, (await SyncAsync()).Sent);
        Assert.Equal(2, _calendar.Inserts);
    }

    [Fact]
    public async Task MovesAnEcheanceThatMoved()
    {
        var deadline = Deadline();
        await SyncAsync();

        deadline.Date = new DateOnly(2026, 11, 19);
        await _vault.Database.SaveChangesAsync();

        Assert.Equal(1, (await SyncAsync()).Sent);

        var single = Assert.Single(_calendar.Events);
        Assert.Equal(new DateOnly(2026, 11, 19), single.Value.Date);
        Assert.Equal(1, _calendar.Inserts);
    }

    [Fact]
    public async Task TakesAwayWhatWasDeletedInTheDossier()
    {
        var deadline = Deadline();
        await SyncAsync();

        _vault.Database.Deadlines.Remove(deadline);
        await _vault.Database.SaveChangesAsync();

        Assert.Equal(1, (await SyncAsync()).Sent);
        Assert.Empty(_calendar.Events);
        Assert.Empty(await _vault.Database.AgendaEvents.ToListAsync());
    }

    /// <summary>
    /// Closing a dossier hides its échéances in Avocado. Removing them from her agenda would erase
    /// last year's audiences from a record of where she was, so what is there stays.
    /// </summary>
    [Fact]
    public async Task LeavesAClosedDossiersEcheancesInTheCalendar()
    {
        Deadline();
        await SyncAsync();

        _matter.ClosedOn = new DateOnly(2026, 10, 4);
        await _vault.Database.SaveChangesAsync();

        await SyncAsync();

        Assert.Single(_calendar.Events);
    }

    /// <summary>
    /// « Faite » is not « annulée ». It happened, it stays in her agenda, and it stops blocking the
    /// slot so the day reads as free.
    /// </summary>
    [Fact]
    public async Task KeepsAnEcheanceThatWasDone()
    {
        var deadline = Deadline();
        await SyncAsync();

        deadline.IsDone = true;
        await _vault.Database.SaveChangesAsync();

        await SyncAsync();

        Assert.Single(_calendar.Events);
        Assert.True(Assert.Single(_calendar.Events).Value.IsDone);
    }

    /// <summary>
    /// She deleted the event in Google. The dossier is the record, so it goes back: an échéance that
    /// silently stopped appearing is exactly the failure this feature exists to prevent.
    /// </summary>
    [Fact]
    public async Task PutsBackAnEventSheDeletedInGoogle()
    {
        var deadline = Deadline();
        await SyncAsync();

        _calendar.Events.Clear();

        deadline.Label = "Audience de plaidoirie";
        await _vault.Database.SaveChangesAsync();

        await SyncAsync();

        Assert.Single(_calendar.Events);
        Assert.Equal(2, _calendar.Inserts);
    }

    /// <summary>
    /// Five failures, then it waits to be asked. A loop retrying a revoked token every quarter of an
    /// hour for a week burns her quota and tells nobody.
    /// </summary>
    [Fact]
    public async Task GivesUpAfterFiveFailuresAndTriesAgainWhenAsked()
    {
        Deadline();
        _calendar.Fails = true;

        for (var attempt = 0; attempt < AgendaSettingKeys.MaxFailures; attempt++)
        {
            Assert.True((await SyncAsync()).Ran);
        }

        var stopped = await _connection.ReadAsync(_vault.Database, default);
        Assert.True(stopped.HasStopped);
        Assert.Equal("Google a répondu 403.", stopped.Error);

        // It has stopped trying on its own.
        Assert.False((await SyncAsync()).Ran);

        // « Synchroniser » forces one, and a pass that works clears the count and the message.
        _calendar.Fails = false;
        Assert.True((await SyncAsync(force: true)).Ran);

        var recovered = await _connection.ReadAsync(_vault.Database, default);
        Assert.False(recovered.HasStopped);
        Assert.Null(recovered.Error);
        Assert.NotNull(recovered.SyncedAt);
    }

    /// <summary>Paused keeps the connection and stops the pushing.</summary>
    [Fact]
    public async Task SendsNothingWhilePaused()
    {
        Deadline();
        await _connection.SetEnabledAsync(_vault.Database, false, default);

        Assert.False((await SyncAsync()).Ran);
        Assert.Empty(_calendar.Events);
    }

    /// <summary>
    /// Disconnecting forgets which events were pushed, because those ids name events in a calendar
    /// Avocado can no longer reach. Reconnecting otherwise believes everything is already there and
    /// sends nothing.
    /// </summary>
    [Fact]
    public async Task ForgetsThePushedEventsOnDisconnect()
    {
        Deadline();
        await SyncAsync();

        await _connection.DisconnectAsync(_vault.Database, default);

        Assert.Empty(await _vault.Database.AgendaEvents.ToListAsync());

        var state = await _connection.ReadAsync(_vault.Database, default);
        Assert.False(state.IsConnected);

        // And what is in her Google calendar is left alone: disconnecting is not an instruction to
        // delete a year of her agenda.
        Assert.Single(_calendar.Events);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _vault.Dispose();
    }
}

/// <summary>A calendar that remembers what it was told, and can be made to refuse.</summary>
internal sealed class FakeCalendar : IGoogleCalendar
{
    public const string Id = "cal-1";

    private int _next;

    public Dictionary<string, AgendaEntry> Events { get; } = [];

    public int Inserts { get; private set; }

    public bool Fails { get; set; }

    public Task<string> EnsureCalendarAsync(string accessToken, string? calendarId, CancellationToken ct) =>
        Task.FromResult(calendarId ?? Id);

    public Task<string> InsertAsync(string accessToken, string calendarId, AgendaEntry entry, CancellationToken ct)
    {
        Refuse();

        var id = $"evt-{++_next}";
        Events[id] = entry;
        Inserts++;

        return Task.FromResult(id);
    }

    public Task UpdateAsync(
        string accessToken, string calendarId, string eventId, AgendaEntry entry, CancellationToken ct)
    {
        Refuse();

        if (!Events.ContainsKey(eventId))
        {
            throw new AgendaEventGoneException();
        }

        Events[eventId] = entry;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string accessToken, string calendarId, string eventId, CancellationToken ct)
    {
        Refuse();
        Events.Remove(eventId);

        return Task.CompletedTask;
    }

    public Task<string?> AccountAsync(string accessToken, CancellationToken ct) =>
        Task.FromResult<string?>("cabinet@example.fr");

    private void Refuse()
    {
        if (Fails)
        {
            throw new GoogleAgendaException("Google a répondu 403.");
        }
    }
}

/// <summary>
/// Google's token endpoint, answering what Google answers.
///
/// <para>Stubbed at the transport rather than above it, so the real exchange code runs: the form it
/// posts, the JSON it reads, and the margin it takes off the expiry are all under test here.</para>
/// </summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new TokenHandler());

    private sealed class TokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"ya29.test","expires_in":3600}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
    }
}
