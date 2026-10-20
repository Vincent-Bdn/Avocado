using System.Text;
using Avocado.Server.Data;
using Avocado.Server.Features.Matters;
using Avocado.Vault;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Folders;

/// <param name="RelativePath">The file in her folder, as the Documents tab listed it.</param>
/// <param name="Label">The libellé written for the judge. Empty falls back to the file's own name.</param>
/// <param name="Suffix">
/// « bis », « ter », « 1 ». A pièce carrying one stays on the number of the line before it instead of
/// consuming a new one, which is what « bis » means: this document belongs with 68.
/// </param>
public sealed record PieceToVerse(string RelativePath, string? Label, string? Suffix = null);

/// <param name="StartNumber">Null continues from the highest number already used.</param>
/// <param name="Stamp">False copies without stamping, which is what happens with no stamp image.</param>
/// <param name="Merge">Also write one PDF holding every pièce of this batch, in order.</param>
public sealed record VerserPiecesInput(
    IReadOnlyList<PieceToVerse> Pieces,
    int? StartNumber = null,
    bool Stamp = true,
    bool Merge = false);

/// <param name="Number">« 12 », « 68 bis », as it is written on the pièce and in the bordereau.</param>
/// <param name="Error">Why this one was not versed. Null when it was.</param>
public sealed record VersedPiece(string Number, string Name, string RelativePath, bool Stamped, string? Error);

public sealed record VerserPiecesResult(
    IReadOnlyList<VersedPiece> Pieces,
    string? BordereauPath,
    string? MergedPath,
    int Failed);

/// <summary>
/// Turns a selection of documents into numbered pièces, stamped, with their bordereau.
///
/// <para><b>Selected, not imported.</b> The cabinet's own tool asked her to drag files into it,
/// because that tool was a page on its own with no idea what a dossier was. Here the documents are
/// already in the dossier and already on screen, so the gesture is to tick them: asking her to drop
/// files that Avocado is currently listing would be asking her to fetch something she is holding.</para>
///
/// <para><b>Numbered in the order she put them in.</b> Not alphabetically and not by date: a
/// bordereau's order is an argument, the pièces are cited in that order in the conclusions, and it is
/// hers to decide. Numbering continues past the highest already used, never filling a gap, because a
/// withdrawn pièce n° 2 may already be cited in something filed.</para>
///
/// <para><b>One failure does not lose the batch.</b> A .docx among the PDFs, a file she moved between
/// selecting and confirming, a scan that turns out to be corrupt: that one is reported and the rest
/// are versed. Fifteen pièces due this afternoon is not the moment to make her start again.</para>
/// </summary>
public static class VerserPieces
{
    /// <summary>The bordereau's file name, in the Pièces folder beside what it lists.</summary>
    public const string BordereauName = "Bordereau de communication de pièces.txt";

    public static async Task<IResult> HandleAsync(
        Guid matterId,
        VerserPiecesInput input,
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        var matter = await database.Matters
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == matterId, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        if (matter.DocumentsFolder is not { Length: > 0 } root || !Directory.Exists(root))
        {
            return Problem("Ce dossier n'a pas encore de dossier de documents.");
        }

        if (input.Pieces.Count == 0)
        {
            return Problem("Aucun fichier sélectionné.");
        }

        var exhibits = Path.Combine(root, Exhibits.Folder);
        Directory.CreateDirectory(exhibits);

        var stampImage = input.Stamp
            ? await CabinetStamp.ReadAsync(database, vaults, cancellationToken).ConfigureAwait(false)
            : null;

        var options = stampImage is null
            ? new StampOptions()
            : await CabinetStamp.OptionsAsync(database, cancellationToken).ConfigureAwait(false);

        // Both folders, so a dossier versed under the old name keeps counting rather than restarting
        // at 1 and writing a second « Pièce 1 » beside last year's.
        var number = input.StartNumber ?? Exhibits.NextNumber(Exhibits.FileNamesIn(root));

        var versed = new List<VersedPiece>();
        var stampedBytes = new List<byte[]>();

        // Whether the current integer has been handed to an ordinary pièce yet. A suffixed one stays
        // on it — « 68 » then « 68 bis » — and an ordinary one moves to the next. See Numbering.
        var used = false;

        foreach (var piece in input.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var suffix = Exhibits.CleanSuffix(piece.Suffix);

            if (suffix is null && used)
            {
                number++;
                used = false;
            }

            var outcome = await OneAsync(
                root, exhibits, piece, new ExhibitNumber(number, suffix),
                stampImage, options, stampedBytes, cancellationToken)
                .ConfigureAwait(false);

            versed.Add(outcome);

            // The number is consumed only by a pièce that was actually written. A file that failed
            // must not leave a hole in the numbering that nothing accounts for.
            if (outcome.Error is null && suffix is null)
            {
                used = true;
            }
        }

        var written = versed.Where(piece => piece.Error is null).ToList();

        var bordereau = written.Count == 0
            ? null
            : await WriteBordereauAsync(exhibits, matter, written, cancellationToken).ConfigureAwait(false);

        var merged = input.Merge && stampedBytes.Count > 0
            ? WriteMerged(exhibits, matter, stampedBytes)
            : null;

        return Results.Ok(new VerserPiecesResult(
            versed,
            bordereau,
            merged,
            versed.Count(piece => piece.Error is not null)));
    }

    private static async Task<VersedPiece> OneAsync(
        string root,
        string exhibits,
        PieceToVerse piece,
        ExhibitNumber number,
        byte[]? stampImage,
        StampOptions options,
        List<byte[]> stamped,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(
            Path.Combine(root, piece.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        // The path comes from the window, so it is checked like any other path from there.
        if (!DossierFolderReader.Inside(root, source) || !File.Exists(source))
        {
            return new VersedPiece(number.ToString(), piece.RelativePath, piece.RelativePath, false,
                "Ce fichier n'est plus dans le dossier.");
        }

        var original = Path.GetFileName(source);
        var canStamp = stampImage is not null && PieceStamp.CanStamp(original);

        // A photograph is turned into a page before it is stamped, so what lands in Pièces is a PDF
        // and the name has to say so. Without stamping it is copied untouched, extension included.
        var naming = canStamp && PieceStamp.IsPicture(original)
            ? Path.ChangeExtension(original, ".pdf")
            : original;

        var name = Exhibits.FileName(number, piece.Label?.Trim(), naming);
        var destination = Path.Combine(exhibits, name);

        try
        {
            if (canStamp)
            {
                var read = await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);

                var bytes = PieceStamp.Apply(
                    PieceStamp.IsPicture(original) ? PieceStamp.PageFromPicture(read) : read,
                    stampImage!,
                    number.ToString(),
                    options);

                // CreateNew, never overwrite: two pièces never share a number, so something already
                // under this one is an exhibit that may have been communicated.
                var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);

                await using (output.ConfigureAwait(false))
                {
                    await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }

                stamped.Add(bytes);
            }
            else
            {
                File.Copy(source, destination, overwrite: false);
            }

            return new VersedPiece(number.ToString(), name, $"{Exhibits.Folder}/{name}", canStamp, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new VersedPiece(number.ToString(), name, piece.RelativePath, false, exception.Message);
        }
        catch (Exception exception)
        {
            // A PDF that will not open. Named, and the batch carries on.
            return new VersedPiece(number.ToString(), name, piece.RelativePath, false,
                $"Ce PDF n'a pas pu être tamponné : {exception.Message}");
        }
    }

    /// <summary>
    /// The list that proves what was communicated and when.
    ///
    /// <para>Plain text with a BOM, which is what her own tool produced and is not an arbitrary
    /// choice: without it Windows opens a UTF-8 file as ANSI and « Pièce » arrives as « PiÃ¨ce » in
    /// front of a magistrate.</para>
    /// </summary>
    private static async Task<string> WriteBordereauAsync(
        string exhibits,
        Matter matter,
        IReadOnlyList<VersedPiece> pieces,
        CancellationToken cancellationToken)
    {
        var text = new StringBuilder();

        text.AppendLine("BORDEREAU DE COMMUNICATION DE PIÈCES");
        text.AppendLine();
        text.AppendLine($"Dossier : {matter.Reference} - {matter.Name}");

        if (matter.CourtCaseNumber is { Length: > 0 } rg)
        {
            text.AppendLine($"N° RG : {rg}");
        }

        text.AppendLine($"Établi le {DateTime.Now:dd/MM/yyyy}");
        text.AppendLine();

        foreach (var piece in pieces)
        {
            text.AppendLine($"{piece.Number}. {Exhibits.LabelOf(piece.Name) ?? piece.Name}");
        }

        var path = Path.Combine(exhibits, BordereauName);

        // The BOM, deliberately. See above.
        await File.WriteAllTextAsync(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken)
            .ConfigureAwait(false);

        return $"{Exhibits.Folder}/{BordereauName}";
    }

    private static string? WriteMerged(string exhibits, Matter matter, IReadOnlyList<byte[]> documents)
    {
        try
        {
            var name = PortableFileName.Clean($"Pièces {matter.Reference}.pdf", '-');
            var path = Path.Combine(exhibits, name);

            File.WriteAllBytes(path, PieceStamp.Merge(documents));

            return $"{Exhibits.Folder}/{name}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The pièces themselves are written; the convenience copy is not worth failing over.
            return null;
        }
    }

    private static IResult Problem(string detail) =>
        Results.Problem(title: "Versement impossible", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
