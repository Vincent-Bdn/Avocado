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
        group.MapGet("/matters/{matterId:guid}/exhibits/next-number", NextNumberAsync);
        group.MapPost("/matters/{matterId:guid}/exhibits/verser", VerserAsync);
        group.MapPost("/matters/{matterId:guid}/exhibits/verser-batch", VerserPieces.HandleAsync);

        return routes;
    }

    /// <summary>
    /// The number the next pièce would take, and the ones a withdrawal left free.
    ///
    /// <para>So the window can show real numbers down the versement panel rather than a blank column
    /// and a field whose placeholder said « suite ». She asked for the opposite of a placeholder:
    /// « plutôt que suite, indiquer le numéro de départ, et par défaut c'est le n° 1 ». A number she
    /// can see is a number she can correct.</para>
    ///
    /// <para>Read off the folder, never from a counter, so that renaming or deleting a pièce in
    /// Explorer leaves Avocado agreeing with what is on disk.</para>
    /// </summary>
    private static async Task<IResult> NextNumberAsync(
        Guid matterId,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var found = await FolderOfAsync(database, matterId, cancellationToken);

        if (found is null)
        {
            return Results.NotFound();
        }

        // A dossier with no folder yet, or no Pièces folder in it, starts at one. Not an error: it is
        // the state every dossier is in before its first versement, and the answer is known.
        var names = found.Value.Folder is { Length: > 0 } root
            && Directory.Exists(System.IO.Path.Combine(root, Exhibits.Folder))
            ? Directory.EnumerateFiles(System.IO.Path.Combine(root, Exhibits.Folder))
                .Select(System.IO.Path.GetFileName)
                .OfType<string>()
                .ToList()
            : [];

        return Results.Ok(new
        {
            next = Exhibits.NextNumber(names),
            free = Exhibits.FreeNumbers(names),
        });
    }

    /// <param name="q">
    /// When given, the answer is every matching file in the whole dossier rather than one level of
    /// it. The tree loads a level at a time, so a search done in the window would only ever see the
    /// folders already opened.
    /// </param>
    private static async Task<IResult> ListAsync(
        Guid matterId,
        string? path,
        string? q,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var folder = await FolderOfAsync(database, matterId, cancellationToken);

        if (folder is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(string.IsNullOrWhiteSpace(q)
            ? DossierFolderReader.Read(folder.Value.Folder, path, cancellationToken)
            : DossierFolderReader.Search(folder.Value.Folder, q.Trim(), take: 200, cancellationToken));
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
