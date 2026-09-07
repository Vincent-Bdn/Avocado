using Avocado.Server.Data;
using Avocado.Server.Features.Matters;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Folders;

/// <param name="Path">Absolute, as she picked it. Empty or null detaches the dossier from its folder.</param>
public sealed record SetFolderInput(string? Path);

/// <param name="RelativePath">The file to verser, from the dossier's folder.</param>
/// <param name="Label">The libellé written for the judge. Optional at the moment of versement.</param>
public sealed record VerserInput(string RelativePath, string? Label);

public static class FolderEndpoints
{
    public static IEndpointRouteBuilder MapDossierFolders(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api").WithTags("Documents");

        group.MapGet("/matters/{matterId:guid}/folder", ListAsync);
        group.MapPut("/matters/{matterId:guid}/folder", SetAsync);
        group.MapPost("/matters/{matterId:guid}/exhibits/verser", VerserAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        Guid matterId,
        string? path,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var folder = await FolderOfAsync(database, matterId, cancellationToken);

        return folder is null
            ? Results.NotFound()
            : Results.Ok(DossierFolderReader.Read(folder.Value.Folder, path, cancellationToken));
    }

    private static async Task<IResult> SetAsync(
        Guid matterId,
        SetFolderInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(input.Path))
        {
            matter.DocumentsFolder = null;
            await database.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }

        var folder = System.IO.Path.GetFullPath(input.Path.Trim());

        if (!Directory.Exists(folder))
        {
            return Problem("Ce dossier n'existe pas, ou n'est pas accessible depuis cet ordinateur.");
        }

        // Two dossiers on one folder would each show the other's files, and versing a pièce in one
        // would number a file the other also lists. Nothing here could tell them apart afterwards.
        var taken = await database.Matters
            .Where(candidate => candidate.Id != matterId && candidate.DocumentsFolder != null)
            .Select(candidate => new { candidate.Id, candidate.Reference, candidate.Name, candidate.DocumentsFolder })
            .ToListAsync(cancellationToken);

        if (taken.FirstOrDefault(other => Same(other.DocumentsFolder!, folder)) is { } clash)
        {
            return Problem($"Ce dossier est déjà celui de « {clash.Reference} {clash.Name} ».");
        }

        matter.DocumentsFolder = folder;
        matter.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Verser un document comme pièce: Avocado copies it into « Pièces » under the next free number.
    ///
    /// <para><b>A copy, and deliberately not a move or a link.</b> The original stays where she filed
    /// it, because it is hers and because a pièce is a different object: it is the numbered exhibit
    /// communicated to the other side, and it must not change afterwards when she edits her working
    /// copy. Both are readable files in her own folder, which is the whole point of this change.</para>
    ///
    /// <para>The number comes from the files already there rather than from a counter in the database.
    /// She can rename or delete one in Explorer, and what is on disk is then still the truth.</para>
    /// </summary>
    private static async Task<IResult> VerserAsync(
        Guid matterId,
        VerserInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var found = await FolderOfAsync(database, matterId, cancellationToken);

        if (found is null)
        {
            return Results.NotFound();
        }

        if (found.Value.Folder is not { Length: > 0 } root || !Directory.Exists(root))
        {
            return Problem("Ce dossier n'a pas encore de dossier de documents.");
        }

        var source = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(root, input.RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        if (!DossierFolderReader.Inside(root, source) || !File.Exists(source))
        {
            return Problem("Ce fichier n'est pas dans le dossier.");
        }

        var exhibits = System.IO.Path.Combine(root, Exhibits.Folder);
        Directory.CreateDirectory(exhibits);

        var number = Exhibits.NextNumber(
            Directory.EnumerateFiles(exhibits).Select(System.IO.Path.GetFileName).OfType<string>());

        var name = Exhibits.FileName(number, input.Label?.Trim(), System.IO.Path.GetFileName(source));
        var destination = System.IO.Path.Combine(exhibits, name);

        try
        {
            // overwrite: false. Two pièces never share a number, so a collision means something is
            // already there under this one, and silently replacing it would destroy an exhibit that
            // may already have been communicated.
            File.Copy(source, destination, overwrite: false);
        }
        catch (IOException exception)
        {
            return Problem($"La pièce n'a pas pu être écrite : {exception.Message}");
        }

        return Results.Ok(new
        {
            number,
            name,
            relativePath = $"{Exhibits.Folder}/{name}",
        });
    }

    private static bool Same(string left, string right) =>
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(left))
            .Equals(
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);

    private static async Task<(Guid Id, string? Folder)?> FolderOfAsync(
        AvocadoDbContext database,
        Guid matterId,
        CancellationToken cancellationToken)
    {
        var found = await database.Matters
            .AsNoTracking()
            .Where(matter => matter.Id == matterId)
            .Select(matter => new { matter.Id, matter.DocumentsFolder })
            .FirstOrDefaultAsync(cancellationToken);

        return found is null ? null : (found.Id, found.DocumentsFolder);
    }

    private static IResult Problem(string detail) =>
        Results.Problem(title: "Dossier de documents", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
