using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Avocado.Server.Features.Agenda.Infrastructure;

/// <param name="ClientId">Empty when nobody has configured one, which is the default.</param>
public sealed record GoogleCredentials(string ClientId, string ClientSecret)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}

public sealed record GoogleTokens(string? RefreshToken, string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// The authorisation code flow for an installed application, with PKCE, against a loopback redirect.
///
/// <para><b>Why loopback and not a device code.</b> Avocado already runs an HTTP server on 127.0.0.1
/// and the shell already knows how to open a browser, so the redirect comes back to a port this
/// process owns. Nothing is pasted, and no code travels through anywhere a screen-sharing session or
/// a clipboard manager could keep it.</para>
///
/// <para><b>The scope is the narrow one.</b> <c>calendar.app.created</c> lets Avocado create a
/// calendar and manage the events in it, and gives it no way to read, change or delete anything in
/// the calendars the cabinet already has. A lawyer's agenda holds her clients' names and where she
/// will be; asking for all of it to write twelve échéances would be asking for far more than the job
/// needs, and the consent screen would rightly say so.</para>
/// </summary>
public sealed class GoogleOAuth(IHttpClientFactory factory)
{
    public const string Scope = "https://www.googleapis.com/auth/calendar.app.created";

    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// The verifiers for flows that have been started and not yet come back, by state.
    ///
    /// <para>In memory on purpose: a flow that does not complete before Avocado is closed is one she
    /// has to start again, which is a second click and is the correct outcome. Writing them down
    /// would mean persisting a credential for an authorisation nobody asked to resume.</para>
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _pending = new();

    public string Start(GoogleCredentials credentials, string redirectUri)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        _pending[state] = verifier;

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = credentials.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            // Without both of these Google returns no refresh token on a re-authorisation, and the
            // connection silently becomes one that stops working in an hour.
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };

        return QueryHelpers.AddQueryString(AuthorizeEndpoint, query);
    }

    /// <summary>Exchanges the code the browser came back with. Null when the state is not one of ours.</summary>
    public async Task<GoogleTokens?> CompleteAsync(
        GoogleCredentials credentials,
        string state,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove(state, out var verifier))
        {
            return null;
        }

        return await ExchangeAsync(credentials, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
        }, cancellationToken);
    }

    public Task<GoogleTokens?> RefreshAsync(
        GoogleCredentials credentials,
        string refreshToken,
        CancellationToken cancellationToken) =>
        ExchangeAsync(credentials, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancellationToken);

    private async Task<GoogleTokens?> ExchangeAsync(
        GoogleCredentials credentials,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        form["client_id"] = credentials.ClientId;

        if (!string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            form["client_secret"] = credentials.ClientSecret;
        }

        using var client = factory.CreateClient(nameof(GoogleOAuth));

        using var response = await client.PostAsync(
            TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleAgendaException(Describe(body, response.StatusCode));
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var access = root.TryGetProperty("access_token", out var token) ? token.GetString() : null;

        if (access is null)
        {
            throw new GoogleAgendaException("Google n’a pas renvoyé de jeton d’accès.");
        }

        var seconds = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;

        return new GoogleTokens(
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            access,
            // A minute's margin, so a token that expires while a pass is running is renewed before
            // the call rather than discovered by a 401 halfway through it.
            DateTimeOffset.UtcNow.AddSeconds(seconds - 60));
    }

    /// <summary>
    /// Google's error, in French where it is one of the three that actually happen, and verbatim
    /// otherwise. « invalid_grant » on a refresh is the one a lawyer will meet: she revoked the
    /// access, or changed her password, and the answer is to connect again rather than to wait.
    /// </summary>
    internal static string Describe(string body, System.Net.HttpStatusCode status)
    {
        var code = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);

            code = document.RootElement.TryGetProperty("error", out var error)
                ? error.ValueKind == JsonValueKind.String ? error.GetString() ?? string.Empty : string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            // Not JSON. Google's gateway errors are HTML, and the status is the only fact in them.
        }

        return code switch
        {
            "invalid_grant" => "L’accès a été retiré côté Google. Reconnectez le compte.",
            "invalid_client" => "L’identifiant OAuth est incorrect. Vérifiez-le dans les réglages.",
            "access_denied" => "L’autorisation a été refusée.",
            _ => $"Google a répondu {(int)status}.",
        };
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Something Google said no to, phrased for the person reading Réglages.</summary>
public sealed class GoogleAgendaException(string message) : Exception(message);
