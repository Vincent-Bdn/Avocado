using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Server.Features.Templates.Infrastructure;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Templates.Endpoints;

public sealed record GenerateInput(string? FileName, string? Folder);

/// <summary>
/// Fills a modèle with this dossier's own wording and writes the result into the dossier's folder.
///
/// <para>Into her folder rather than into a download or a coffre, because the point is that she opens
/// it and finishes the sentences Word cannot write for her: a generated letter is a draft, and a draft
/// belongs where she works. It appears beside her other files, under a name she can change.</para>
///
/// <para>A dossier with no folder cannot receive one. Saying so is better than writing it somewhere
/// she would have to be told about.</para>
/// </summary>
public static class GenerateFromTemplate
{
    public static async Task<IResult> HandleAsync(
        Guid matterId,
        Guid templateId,
        GenerateInput input,
        AvocadoDbContext database,
        IVaultStore vaultStore,
        TenantContext tenant,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .AsNoTracking()
            .Include(candidate => candidate.Parties)
            .ThenInclude(party => party.Contact)
            .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var template = await database.Templates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == templateId, cancellationToken);

        if (template is null)
        {
            return Results.NotFound();
        }

        var vault = vaultStore.Get(tenant.VaultId);
        var client = matter.Parties.FirstOrDefault(party => party.IsClient)?.Contact;
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        byte[] filled;
        await using (var source = vault.Blobs.OpenRead(new BlobReference(template.BlobSha256, template.SizeBytes)))
        {
            filled = TemplateMerge.Fill(source, TemplateFields.For(matter, client, today));
        }

        if (matter.DocumentsFolder is not { Length: > 0 } root || !Directory.Exists(root))
        {
            return Results.Problem(
                title: "Ce dossier n'a pas de dossier de documents",
                detail: "Indiquez où vivent ses documents, et le modèle sera écrit là.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var name = string.IsNullOrWhiteSpace(input.FileName)
            ? $"{Slug(template.Name)}-{matter.Reference}.docx"
            : EnsureDocx(input.FileName.Trim());

        var into = string.IsNullOrWhiteSpace(input.Folder)
            ? root
            : Path.GetFullPath(Path.Combine(root, input.Folder.Trim().Replace('/', Path.DirectorySeparatorChar)));

        // The subfolder comes from the window, so it is checked like any other path from there.
        if (!DossierFolderReader.Inside(root, into))
        {
            into = root;
        }

        Directory.CreateDirectory(into);

        // Never over a file that is already there: she may have generated this letter last week and
        // spent an afternoon on it since.
        var destination = Free(Path.Combine(into, name));

        await File.WriteAllBytesAsync(destination, filled, cancellationToken);

        return Results.Created(
            $"/api/matters/{matterId}/folder",
            new { path = destination, fileName = Path.GetFileName(destination) });
    }

    /// <summary>« lettre.docx », then « lettre (2).docx ». Nothing she has written is overwritten.</summary>
    private static string Free(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({index}){extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    private static string EnsureDocx(string name) =>
        name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.docx";

    /// <summary>A file name, not a URL slug: accents stay, only what a filesystem refuses is replaced.</summary>
    private static string Slug(string name) =>
        PortableFileName.Clean(name, '-')
            .Replace(' ', '-')
            .ToLowerInvariant();
}
