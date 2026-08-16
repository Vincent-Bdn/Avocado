using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Checkout;

/// <param name="Changes">Empty while nothing has moved. What the review shows at the end.</param>
/// <param name="AwaitingDecision">
/// The folder changed while Avocado was not running and she has not said what to do about it. The
/// background sweep leaves it alone until she has.
/// </param>
public sealed record CheckoutView(
    Guid MatterId,
    string FolderPath,
    DateTimeOffset OpenedAt,
    DateTimeOffset? SyncedAt,
    int FileCount,
    bool AwaitingDecision,
    IReadOnlyList<CheckoutChange> Changes);

/// <param name="Resumption">Null unless this folder changed while Avocado was not running.</param>
public sealed record ResumeView(Guid MatterId, string FolderPath, CheckoutResumption Resumption);

public static class CheckoutEndpoints
{
    public static IEndpointRouteBuilder MapCheckouts(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api").WithTags("Checkout");

        group.MapGet("/checkouts", ListAsync);
        group.MapPost("/matters/{matterId:guid}/checkout", OpenAsync);
        group.MapPost("/matters/{matterId:guid}/checkout/sync", SyncAsync);
        group.MapPost("/matters/{matterId:guid}/checkout/resolve", ResolveAsync);
        group.MapDelete("/matters/{matterId:guid}/checkout", CloseAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        AvocadoDbContext database,
        MatterCheckoutService checkouts,
        CancellationToken cancellationToken)
    {
        var open = await database.MatterCheckouts.ToListAsync(cancellationToken).ConfigureAwait(false);
        var views = new List<CheckoutView>();

        foreach (var checkout in open)
        {
            var changes = await checkouts.InspectAsync(checkout, cancellationToken).ConfigureAwait(false);

            views.Add(new CheckoutView(
                checkout.MatterId,
                checkout.FolderPath,
                checkout.OpenedAt,
                checkout.SyncedAt,
                changes.Count,
                checkout.AwaitingDecision,
                CheckoutReconciler.Notable(changes)));
        }

        return Results.Ok(views);
    }

    /// <summary>
    /// Opens the dossier as a folder and shows it, because a folder someone has to go and find is a
    /// folder they will not use.
    /// </summary>
    private static async Task<IResult> OpenAsync(
        Guid matterId,
        MatterCheckoutService checkouts,
        CancellationToken cancellationToken)
    {
        var checkout = await checkouts.OpenAsync(matterId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { checkout.MatterId, checkout.FolderPath });
    }

    /// <summary>
    /// Writes back what changed. Deletions are never applied here: a file that vanishes while she
    /// works is far more likely to be Word rewriting it than a decision.
    /// </summary>
    private static async Task<IResult> SyncAsync(
        Guid matterId,
        MatterCheckoutService checkouts,
        CancellationToken cancellationToken) =>
        Results.Ok(CheckoutReconciler.Notable(
            await checkouts.SyncAsync(matterId, applyDeletions: false, cancellationToken).ConfigureAwait(false)));

    /// <summary>
    /// Her answer after a restart found the folder changed. « keepFolder » writes what is on disk into
    /// the vault; otherwise the folder is written again from the vault and what was in it is dropped.
    /// </summary>
    private static async Task<IResult> ResolveAsync(
        Guid matterId,
        bool keepFolder,
        MatterCheckoutService checkouts,
        CancellationToken cancellationToken)
    {
        await checkouts.ResolveAsync(matterId, keepFolder, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>« J'ai terminé »: last sync with deletions applied, then the folder goes.</summary>
    private static async Task<IResult> CloseAsync(
        Guid matterId,
        MatterCheckoutService checkouts,
        CancellationToken cancellationToken) =>
        Results.Ok(CheckoutReconciler.Notable(
            await checkouts.CloseAsync(matterId, cancellationToken).ConfigureAwait(false)));
}
