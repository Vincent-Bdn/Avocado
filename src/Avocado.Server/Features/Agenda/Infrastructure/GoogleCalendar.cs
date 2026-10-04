using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Avocado.Server.Features.Agenda.ValueObjects;

namespace Avocado.Server.Features.Agenda.Infrastructure;

/// <summary>
/// The four calls this needs, behind an interface so what to send can be tested without a network.
/// </summary>
public interface IGoogleCalendar
{
    /// <summary>The calendar Avocado writes to, created if the id it was given no longer exists.</summary>
    Task<string> EnsureCalendarAsync(string accessToken, string? calendarId, CancellationToken cancellationToken);

    /// <summary>Returns Google's id for the new event.</summary>
    Task<string> InsertAsync(
        string accessToken, string calendarId, AgendaEntry entry, CancellationToken cancellationToken);

    Task UpdateAsync(
        string accessToken, string calendarId, string eventId, AgendaEntry entry, CancellationToken cancellationToken);

    Task DeleteAsync(
        string accessToken, string calendarId, string eventId, CancellationToken cancellationToken);

    /// <summary>The address of the connected account, for Réglages to name who is connected.</summary>
    Task<string?> AccountAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed class GoogleCalendar(IHttpClientFactory factory, TimeProvider clock) : IGoogleCalendar
{
    private const string Base = "https://www.googleapis.com/calendar/v3";

    public async Task<string> EnsureCalendarAsync(
        string accessToken, string? calendarId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(calendarId))
        {
            using var existing = await SendAsync(
                accessToken, HttpMethod.Get, $"{Base}/calendars/{Uri.EscapeDataString(calendarId)}",
                null, cancellationToken, allowNotFound: true);

            if (existing.IsSuccessStatusCode)
            {
                return calendarId;
            }

            // Deleted in Google. Making another is the only way back, and losing the old one's
            // contents is already done; what matters is that the échéances reappear.
        }

        using var response = await SendAsync(
            accessToken, HttpMethod.Post, $"{Base}/calendars",
            JsonContent.Create(new
            {
                summary = AgendaSettingKeys.CalendarName,
                description =
                    "Les échéances de vos dossiers, inscrites par Avocado. "
                    + "Ce calendrier est tenu par Avocado : ce que vous y modifiez est écrasé.",
                timeZone = clock.LocalTimeZone.Id,
            }),
            cancellationToken);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.GetProperty("id").GetString()
            ?? throw new GoogleAgendaException("Google n’a pas renvoyé d’identifiant de calendrier.");
    }

    public async Task<string> InsertAsync(
        string accessToken, string calendarId, AgendaEntry entry, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            accessToken, HttpMethod.Post, Events(calendarId),
            JsonContent.Create(Body(entry)), cancellationToken);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.GetProperty("id").GetString()
            ?? throw new GoogleAgendaException("Google n’a pas renvoyé d’identifiant d’événement.");
    }

    public async Task UpdateAsync(
        string accessToken, string calendarId, string eventId, AgendaEntry entry,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            accessToken, HttpMethod.Put, $"{Events(calendarId)}/{Uri.EscapeDataString(eventId)}",
            JsonContent.Create(Body(entry)), cancellationToken, allowNotFound: true);

        // Deleted in her agenda. The next pass sees no fingerprint change and would never put it
        // back, so it is treated as never sent: the caller drops the row and re-inserts.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AgendaEventGoneException();
        }
    }

    public async Task DeleteAsync(
        string accessToken, string calendarId, string eventId, CancellationToken cancellationToken)
    {
        // Gone already is the outcome we wanted, so it is not a failure.
        using var response = await SendAsync(
            accessToken, HttpMethod.Delete, $"{Events(calendarId)}/{Uri.EscapeDataString(eventId)}",
            null, cancellationToken, allowNotFound: true, allowGone: true);
    }

    public async Task<string?> AccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(
                accessToken, HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo",
                null, cancellationToken, allowNotFound: true);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return document.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException)
        {
            // A label on a settings screen. Not knowing it is not a reason to fail the connection.
            return null;
        }
    }

    private static string Events(string calendarId) =>
        $"{Base}/calendars/{Uri.EscapeDataString(calendarId)}/events";

    /// <summary>
    /// The event itself.
    ///
    /// <para>A délai has no time of day, so it is an all-day event: giving it a placeholder of
    /// midnight would put « 00:00 » on her telephone and make the day it falls on ambiguous across a
    /// timezone. An audience has an hour, and is blocked out for one, which is the only honest guess
    /// available and is what she can then stretch.</para>
    ///
    /// <para>Dates are sent without a zone and with the calendar's own zone named beside them: an
    /// audience at 9 h is at 9 h where the tribunal is, not at whatever instant that was in UTC when
    /// the row was written.</para>
    /// </summary>
    private object Body(AgendaEntry entry)
    {
        var zone = clock.LocalTimeZone.Id;

        object start, end;

        if (entry.Time is { } time)
        {
            var from = entry.Date.ToDateTime(time);

            start = new { dateTime = Local(from), timeZone = zone };
            end = new { dateTime = Local(from.Add(AgendaEntry.Duration)), timeZone = zone };
        }
        else
        {
            start = new { date = Day(entry.Date) };
            end = new { date = Day(entry.Date.AddDays(1)) };
        }

        return new
        {
            summary = entry.Summary,
            description = entry.Description,
            start,
            end,
            // Hers, not Google's defaults: an échéance nobody is reminded of is a diary entry.
            reminders = new
            {
                useDefault = false,
                overrides = new[]
                {
                    new { method = "popup", minutes = Math.Max(0, entry.RemindDaysBefore) * 24 * 60 },
                },
            },
            // « Faite » stays in the agenda, crossed out rather than deleted: it happened, and a
            // calendar that erases what happened is worse than one that keeps it.
            status = "confirmed",
            transparency = entry.IsDone ? "transparent" : "opaque",
        };
    }

    private static string Local(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Day(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<HttpResponseMessage> SendAsync(
        string accessToken,
        HttpMethod method,
        string url,
        HttpContent? content,
        CancellationToken cancellationToken,
        bool allowNotFound = false,
        bool allowGone = false)
    {
        using var client = factory.CreateClient(nameof(GoogleCalendar));
        using var request = new HttpRequestMessage(method, url) { Content = content };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode
            || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            || (allowGone && response.StatusCode == HttpStatusCode.Gone))
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();

        throw new GoogleAgendaException(GoogleOAuth.Describe(body, response.StatusCode));
    }
}

/// <summary>The event was deleted in her agenda, so the row standing for it is worthless.</summary>
public sealed class AgendaEventGoneException : Exception;
