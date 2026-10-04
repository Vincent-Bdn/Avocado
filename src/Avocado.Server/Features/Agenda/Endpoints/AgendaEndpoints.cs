using Avocado.Server.Data;
using Avocado.Server.Features.Agenda.Infrastructure;

namespace Avocado.Server.Features.Agenda.Endpoints;

public sealed record AgendaCredentialsInput(string ClientId, string ClientSecret);

public sealed record AgendaEnabledInput(bool Enabled);

/// <summary>
/// Connecting the agenda, and saying how it is going.
///
/// <para>The callback is the only endpoint in Avocado a browser outside the application reaches, and
/// it reaches it on 127.0.0.1 because that is where the OAuth redirect was sent. It returns a page,
/// not JSON: what is reading it is a tab the lawyer now has to close.</para>
/// </summary>
public static class AgendaEndpoints
{
    public static IEndpointRouteBuilder MapAgenda(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/agenda").WithTags("Agenda");

        group.MapGet("/", StatusAsync);
        group.MapPut("/credentials", SetCredentialsAsync);
        group.MapPut("/enabled", SetEnabledAsync);
        group.MapPost("/google/authorize", AuthorizeAsync);
        group.MapPost("/google/disconnect", DisconnectAsync);
        group.MapPost("/sync", SyncAsync);

        // Outside the group: Google redirects a browser here, and the api token middleware has to
        // let it through. See LocalApiTokenMiddleware.
        routes.MapGet("/api/agenda/google/callback", CallbackAsync).WithTags("Agenda");

        return routes;
    }

    private static async Task<IResult> StatusAsync(
        AgendaConnection connection,
        AvocadoDbContext database,
        CancellationToken cancellationToken) =>
        Results.Ok(await connection.ReadAsync(database, cancellationToken));

    private static async Task<IResult> SetCredentialsAsync(
        AgendaCredentialsInput input,
        AgendaConnection connection,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        await connection.SetCredentialsAsync(
            database, input.ClientId, input.ClientSecret, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> SetEnabledAsync(
        AgendaEnabledInput input,
        AgendaConnection connection,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        await connection.SetEnabledAsync(database, input.Enabled, cancellationToken);

        return Results.NoContent();
    }

    /// <summary>Hands back the consent URL; the shell opens it in her own browser.</summary>
    private static async Task<IResult> AuthorizeAsync(
        HttpRequest request,
        AgendaConnection connection,
        GoogleOAuth oauth,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var credentials = await connection.CredentialsAsync(database, cancellationToken);

        if (!credentials.IsConfigured)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Renseignez d’abord un identifiant OAuth Google."],
            });
        }

        return Results.Ok(new { url = oauth.Start(credentials, connection.RedirectUri(request)) });
    }

    private static async Task<IResult> CallbackAsync(
        HttpRequest request,
        AgendaConnection connection,
        GoogleOAuth oauth,
        IGoogleCalendar calendar,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var state = request.Query["state"].ToString();
        var code = request.Query["code"].ToString();
        var denied = request.Query["error"].ToString();

        if (!string.IsNullOrEmpty(denied))
        {
            return Page("Autorisation refusée", "Rien n’a été connecté. Vous pouvez fermer cet onglet.");
        }

        try
        {
            var tokens = await oauth.CompleteAsync(
                await connection.CredentialsAsync(database, cancellationToken),
                state, code, connection.RedirectUri(request), cancellationToken);

            if (tokens is null)
            {
                return Page(
                    "Demande inconnue",
                    "Cette autorisation ne correspond à aucune demande en cours. Recommencez depuis Avocado.");
            }

            await connection.ConnectAsync(database, tokens, calendar, cancellationToken);

            return Page(
                "Agenda connecté",
                "Vos échéances vont apparaître dans Google Agenda. Vous pouvez fermer cet onglet.");
        }
        catch (GoogleAgendaException failure)
        {
            return Page("Connexion impossible", failure.Message);
        }
    }

    private static async Task<IResult> DisconnectAsync(
        AgendaConnection connection,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        await connection.DisconnectAsync(database, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> SyncAsync(
        AgendaSync sync,
        AgendaConnection connection,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        await sync.RunAsync(database, force: true, cancellationToken);

        return Results.Ok(await connection.ReadAsync(database, cancellationToken));
    }

    /// <summary>
    /// What the browser tab shows. Deliberately one sentence and no styling to speak of: it is the
    /// last thing between her and closing the tab, not a screen.
    /// </summary>
    private static IResult Page(string title, string message) =>
        Results.Content(
            $"""
             <!doctype html>
             <html lang="fr">
             <head><meta charset="utf-8"><title>{title}</title></head>
             <body style="font:15px system-ui;margin:0;display:grid;place-items:center;height:100vh">
               <main style="max-width:30rem;padding:2rem;text-align:center">
                 <h1 style="font-size:1.1rem;margin:0 0 .5rem">{title}</h1>
                 <p style="margin:0;color:#555">{message}</p>
               </main>
             </body>
             </html>
             """,
            "text/html; charset=utf-8");
}
