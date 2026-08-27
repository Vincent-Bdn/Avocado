using Avocado.Server.Features.Imports.Infrastructure;

namespace Avocado.Server.Features.Imports.Endpoints;

/// <param name="ArchivedWords">
/// Folder names that mean a dossier is finished. Sent by the caller rather than fixed here, because
/// « CLASSES » is one practice's filing habit and not a standard: another says ARCHIVES, or CLOS, or
/// nothing at all. Empty falls back to <see cref="DossierScan.DefaultArchivedWords"/>.
/// </param>
public sealed record ScanInput(
    string Root,
    IReadOnlyList<string>? ArchivedWords = null,
    IReadOnlyList<string>? Dossiers = null);

/// <param name="Dossiers">
/// The folders she marked, absolute, each becoming one dossier holding everything beneath it.
///
/// <para>This replaced a pair of controls, one to leave a folder out and one to break it into its
/// subfolders, and it replaced them by making both unnecessary: marking the children instead of the
/// parent <em>is</em> the split, and marking nothing leaves it out. Null means she never chose, in
/// which case the scan's own suggestions run.</para>
/// </param>
public sealed record RunInput(
    string Root,
    IReadOnlyList<string>? Dossiers = null,
    IReadOnlyList<string>? ArchivedWords = null);

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImports(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/imports").WithTags("Imports");

        group.MapPost("/scan", Scan);
        group.MapPost("/run", RunAsync);
        group.MapGet("/progress", Progress);
        group.MapPost("/templates", Templates);

        return routes;
    }

    /// <summary>
    /// Reads the export and reports what importing it would do, without writing anything. Thirteen
    /// thousand files is not something to start on trust.
    /// </summary>
    private static IResult Scan(ScanInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Root) || !Directory.Exists(input.Root))
        {
            return Results.Problem(
                title: "Dossier introuvable",
                detail: "Ce dossier n'existe pas ou n'est pas accessible.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "source-missing" });
        }

        var plan = DossierScan.Read(input.Root, input.ArchivedWords, cancellationToken);

        if (plan.Files == 0)
        {
            return Results.Problem(
                title: "Rien à importer",
                detail: "Ce dossier ne contient aucun fichier, à aucun niveau.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "not-an-export" });
        }

        return Results.Ok(plan);
    }

    /// <summary>
    /// Writes the two spreadsheets she fills in, one row per dossier, beside the export.
    ///
    /// <para>The export has no contacts and no billing, so those can only come from her. Handing over
    /// an empty format to invent would be worse than not asking: the templates arrive with every
    /// dossier already named, so the only thing left is what Avocado genuinely does not know.</para>
    /// </summary>
    private static IResult Templates(ScanInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Root) || !Directory.Exists(input.Root))
        {
            return Results.Problem(
                title: "Dossier introuvable",
                detail: "Ce dossier n'existe pas ou n'est pas accessible.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "source-missing" });
        }

        var plan = DossierScan.Read(input.Root, input.ArchivedWords, cancellationToken);
        var written = ImportSidecars.WriteTemplates(
            input.Root, DossierScan.Candidates(plan, input.Dossiers));

        return Results.Ok(new
        {
            written,
            // Nothing is overwritten, so saying which already existed is the difference between
            // "your work is safe" and a moment of panic.
            kept = new[] { ImportSidecars.TiersFileName, ImportSidecars.FacturationFileName }
                .Select(name => Path.Combine(input.Root, name))
                .Where(path => !written.Contains(path))
                .ToArray(),
        });
    }

    /// <summary>
    /// Starts the import and returns immediately. The work is thirteen gigabytes of encryption; a
    /// request that hangs for twenty minutes cannot be told apart from one that has died.
    /// </summary>
    private static IResult RunAsync(
        RunInput input,
        GestisoftImporter importer,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (importer.IsRunning)
        {
            return Results.Problem(
                title: "Un import est déjà en cours",
                detail: "Attendez qu'il se termine avant d'en lancer un autre.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["code"] = "already-running" });
        }

        var plan = DossierScan.Read(input.Root, input.ArchivedWords, cancellationToken);
        var candidates = DossierScan.Candidates(plan, input.Dossiers);

        if (candidates.Count == 0)
        {
            return Results.Problem(
                title: "Aucun dossier sélectionné",
                detail: "Marquez au moins un dossier avant de lancer l'import.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "nothing-chosen" });
        }

        // Not the request's cancellation token: that one is cancelled the moment this returns, which
        // is immediately, and would abort the import before it began.
        var sidecars = ImportSidecars.Read(input.Root);

        _ = Task.Run(() => importer.RunAsync(candidates, sidecars, CancellationToken.None), CancellationToken.None);

        return Results.Accepted(value: new { dossiers = candidates.Count, files = candidates.Sum(c => c.Files) });
    }

    private static IResult Progress(GestisoftImporter importer) =>
        importer.Progress is { } progress ? Results.Ok(progress) : Results.NoContent();
}
