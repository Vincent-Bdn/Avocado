using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Endpoints;

/// <summary>
/// The metadata around a file: its name, the folder it is filed under, its type and its own date. The
/// bytes are never touched — the blob is content-addressed, so renaming a document cannot invalidate
/// what it points at.
/// </summary>
public static class UpdateDocument
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        DocumentInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["document"] = [error] });
        }

        var document = await database.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (document is null)
        {
            return Results.NotFound();
        }

        document.FileName = input.FileName.Trim();
        document.Folder = DocumentFolder.Normalise(input.Folder);
        document.Type = Trimmed(input.Type);
        document.DocumentDate = input.DocumentDate;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A folder is a string, and there is no folder table.
/// <para>
/// It exists exactly as long as a document names it, which is what stops an empty hierarchy
/// accumulating around three files. Nesting is written with « / » and the client groups on the
/// segments; normalising here means « Procédure / », «/Procédure» and « procédure » cannot become
/// three folders that look identical in the list.
/// </para>
/// </summary>
public static class DocumentFolder
{
    public static string? Normalise(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        var segments = folder
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return segments.Length == 0 ? null : string.Join(" / ", segments);
    }
}
