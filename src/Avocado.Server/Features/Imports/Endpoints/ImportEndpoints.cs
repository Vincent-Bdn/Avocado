using Avocado.Server.Features.Imports.Infrastructure;

namespace Avocado.Server.Features.Imports.Endpoints;

public sealed record ScanInput(string Root);

/// <param name="Split">Source paths the user chose to break into one dossier per subfolder.</param>
public sealed record RunInput(string Root, IReadOnlyList<string> Split, IReadOnlyList<string>? Only);

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImports(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/imports").WithTags("Imports");

        group.MapPost("/scan", Scan);
        group.MapPost("/run", RunAsync);
        group.MapGet("/progress", Progress);

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

        var plan = GestisoftScan.Read(input.Root, cancellationToken);

        if (plan.Candidates.Count == 0)
        {
            return Results.Problem(
                title: "Rien à importer",
                detail: "Ce dossier ne contient ni « EN COURS » ni « CLASSES ». Choisissez le dossier " +
                        "qui contient ces deux-là, pas l'un des deux.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "not-an-export" });
        }

        return Results.Ok(plan);
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

        var plan = GestisoftScan.Read(input.Root, cancellationToken);
        var split = new HashSet<string>(input.Split ?? [], StringComparer.OrdinalIgnoreCase);
        var only = input.Only is { Count: > 0 } chosen
            ? new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase)
            : null;

        var candidates = new List<ImportCandidate>();

        foreach (var candidate in plan.Candidates)
        {
            if (only is not null && !only.Contains(candidate.SourcePath))
            {
                continue;
            }

            if (split.Contains(candidate.SourcePath))
            {
                candidates.AddRange(GestisoftScan.Split(candidate, cancellationToken));
            }
            else
            {
                candidates.Add(candidate);
            }
        }

        // Not the request's cancellation token: that one is cancelled the moment this returns, which
        // is immediately, and would abort the import before it began.
        _ = Task.Run(() => importer.RunAsync(candidates, CancellationToken.None), CancellationToken.None);

        return Results.Accepted(value: new { dossiers = candidates.Count, files = candidates.Sum(c => c.Files) });
    }

    private static IResult Progress(GestisoftImporter importer) =>
        importer.Progress is { } progress ? Results.Ok(progress) : Results.NoContent();
}
