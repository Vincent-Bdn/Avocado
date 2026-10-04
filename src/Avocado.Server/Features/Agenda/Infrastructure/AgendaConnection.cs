using System.Globalization;
using Avocado.Server.Data;
using Avocado.Server.Features.Settings;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Agenda.Infrastructure;

/// <param name="IsConfigured">An OAuth client exists. Without one nothing can be connected at all.</param>
/// <param name="Account">The Google address, when it is known.</param>
public sealed record AgendaState(
    bool IsConfigured,
    bool IsConnected,
    bool IsEnabled,
    string? Account,
    DateTimeOffset? SyncedAt,
    string? Error,
    int FailureCount)
{
    /// <summary>It gave up and is waiting to be asked again.</summary>
    public bool HasStopped => FailureCount >= AgendaSettingKeys.MaxFailures;
}

/// <summary>
/// The connection's state and its tokens: reading the settings rows, and keeping one access token in
/// memory for as long as Google says it is good for.
///
/// <para>Singleton for the cached token and nothing else. Everything durable is a settings row, so a
/// restart loses an access token that was going to expire within the hour anyway.</para>
/// </summary>
public sealed class AgendaConnection(GoogleOAuth oauth, IConfiguration configuration, TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public async Task<AgendaState> ReadAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(database, cancellationToken);

        return new AgendaState(
            (await CredentialsAsync(database, cancellationToken)).IsConfigured,
            rows.TryGetValue(AgendaSettingKeys.RefreshToken, out var token) && !string.IsNullOrEmpty(token),
            !rows.TryGetValue(AgendaSettingKeys.Enabled, out var enabled) || enabled != "false",
            rows.GetValueOrDefault(AgendaSettingKeys.Account),
            rows.TryGetValue(AgendaSettingKeys.SyncedAt, out var at)
                && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null,
            rows.GetValueOrDefault(AgendaSettingKeys.Error),
            rows.TryGetValue(AgendaSettingKeys.FailureCount, out var count)
                && int.TryParse(count, CultureInfo.InvariantCulture, out var failures)
                    ? failures
                    : 0);
    }

    /// <summary>
    /// The OAuth client. Configuration first, so a build distributed to cabinets can carry one;
    /// otherwise what she pasted into Réglages.
    /// </summary>
    public async Task<GoogleCredentials> CredentialsAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var fromConfig = configuration["Google:ClientId"];

        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return new GoogleCredentials(fromConfig, configuration["Google:ClientSecret"] ?? string.Empty);
        }

        var rows = await ReadRowsAsync(database, cancellationToken);

        return new GoogleCredentials(
            rows.GetValueOrDefault(AgendaSettingKeys.ClientId) ?? string.Empty,
            rows.GetValueOrDefault(AgendaSettingKeys.ClientSecret) ?? string.Empty);
    }

    public string RedirectUri(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}/api/agenda/google/callback";

    public async Task ConnectAsync(
        AvocadoDbContext database,
        GoogleTokens tokens,
        IGoogleCalendar calendar,
        CancellationToken cancellationToken)
    {
        if (tokens.RefreshToken is null)
        {
            throw new GoogleAgendaException(
                "Google n’a pas renvoyé d’autorisation durable. Révoquez l’accès dans votre compte Google, puis reconnectez.");
        }

        Cache(tokens);

        await WriteAsync(database, cancellationToken, new Dictionary<string, string?>
        {
            [AgendaSettingKeys.RefreshToken] = tokens.RefreshToken,
            [AgendaSettingKeys.Account] = await calendar.AccountAsync(tokens.AccessToken, cancellationToken),
            [AgendaSettingKeys.Enabled] = "true",
            [AgendaSettingKeys.Error] = null,
            [AgendaSettingKeys.FailureCount] = "0",
        });
    }

    /// <summary>
    /// Forgets the connection, and the events with it.
    ///
    /// <para>The rows go because they name events in a calendar Avocado can no longer reach: keeping
    /// them would mean that reconnecting later believed a hundred échéances were already pushed and
    /// sent none of them. What is left in her Google calendar is left alone, deliberately. Avocado is
    /// being disconnected, not given one last instruction to delete a year of her agenda.</para>
    /// </summary>
    public async Task DisconnectAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        Cache(null);

        await database.AgendaEvents.ExecuteDeleteAsync(cancellationToken);

        await WriteAsync(database, cancellationToken, new Dictionary<string, string?>
        {
            [AgendaSettingKeys.RefreshToken] = null,
            [AgendaSettingKeys.CalendarId] = null,
            [AgendaSettingKeys.Account] = null,
            [AgendaSettingKeys.SyncedAt] = null,
            [AgendaSettingKeys.Error] = null,
            [AgendaSettingKeys.FailureCount] = "0",
        });
    }

    public Task SetEnabledAsync(AvocadoDbContext database, bool enabled, CancellationToken cancellationToken) =>
        WriteAsync(database, cancellationToken, new Dictionary<string, string?>
        {
            [AgendaSettingKeys.Enabled] = enabled ? "true" : "false",
            // Turning it back on is also « try again », so the count that stopped it is cleared.
            [AgendaSettingKeys.FailureCount] = enabled ? "0" : null,
            [AgendaSettingKeys.Error] = enabled ? null : null,
        });

    public Task SetCredentialsAsync(
        AvocadoDbContext database,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken) =>
        WriteAsync(database, cancellationToken, new Dictionary<string, string?>
        {
            [AgendaSettingKeys.ClientId] = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim(),
            [AgendaSettingKeys.ClientSecret] =
                string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim(),
        });

    public async Task<string> AccessTokenAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_accessToken is { } cached && clock.GetUtcNow() < _expiresAt)
            {
                return cached;
            }

            var rows = await ReadRowsAsync(database, cancellationToken);
            var refresh = rows.GetValueOrDefault(AgendaSettingKeys.RefreshToken);

            if (string.IsNullOrEmpty(refresh))
            {
                throw new GoogleAgendaException("L’agenda n’est pas connecté.");
            }

            var tokens = await oauth.RefreshAsync(
                await CredentialsAsync(database, cancellationToken), refresh, cancellationToken)
                ?? throw new GoogleAgendaException("Google n’a pas renouvelé l’accès.");

            Cache(tokens);

            return tokens.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> CalendarIdAsync(
        AvocadoDbContext database,
        string accessToken,
        IGoogleCalendar calendar,
        CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(database, cancellationToken);
        var stored = rows.GetValueOrDefault(AgendaSettingKeys.CalendarId);

        var resolved = await calendar.EnsureCalendarAsync(accessToken, stored, cancellationToken);

        if (resolved != stored)
        {
            await WriteAsync(database, cancellationToken, new Dictionary<string, string?>
            {
                [AgendaSettingKeys.CalendarId] = resolved,
            });

            // The calendar is new, so nothing Avocado thinks it pushed is in it.
            await database.AgendaEvents.ExecuteDeleteAsync(cancellationToken);
        }

        return resolved;
    }

    public Task RecordSuccessAsync(
        AvocadoDbContext database, DateTimeOffset at, CancellationToken cancellationToken) =>
        WriteAsync(database, cancellationToken, new Dictionary<string, string?>
        {
            [AgendaSettingKeys.SyncedAt] = at.ToString("O", CultureInfo.InvariantCulture),
            [AgendaSettingKeys.Error] = null,
            [AgendaSettingKeys.FailureCount] = "0",
        });

    public async Task RecordFailureAsync(
        AvocadoDbContext database, string message, CancellationToken cancellationToken)
    {
        var state = await ReadAsync(database, cancellationToken);

        await WriteAsync(database, cancellationToken, new Dictionary<string, string?>
        {
            [AgendaSettingKeys.Error] = message,
            [AgendaSettingKeys.FailureCount] =
                (state.FailureCount + 1).ToString(CultureInfo.InvariantCulture),
        });
    }

    private void Cache(GoogleTokens? tokens)
    {
        _accessToken = tokens?.AccessToken;
        _expiresAt = tokens?.ExpiresAt ?? DateTimeOffset.MinValue;
    }

    private static async Task<Dictionary<string, string>> ReadRowsAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken) =>
        await database.PracticeSettings
            .AsNoTracking()
            .Where(setting => setting.Key.StartsWith("agenda.google."))
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

    /// <summary>A null value removes the row, which is how « absent » is said here.</summary>
    private static async Task WriteAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken,
        Dictionary<string, string?> values)
    {
        foreach (var (key, value) in values)
        {
            var existing = await database.PracticeSettings
                .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken);

            if (value is null)
            {
                if (existing is not null)
                {
                    database.PracticeSettings.Remove(existing);
                }

                continue;
            }

            if (existing is null)
            {
                database.PracticeSettings.Add(new PracticeSetting { Key = key, Value = value });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
