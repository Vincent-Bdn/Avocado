using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Vault;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf;

namespace Avocado.Server.Features.Settings.Endpoints;

/// <param name="Path">A file she picked, read by the backend. The renderer has no filesystem.</param>
public sealed record StampImageInput(string Path);

/// <param name="Configured">Whether something is stored and ready to stamp with.</param>
/// <param name="Svg">Whether that something came from an SVG, which places its own number.</param>
/// <param name="AwaitingRender">
/// An SVG has been stored and Réglages has not yet sent back the rendering of it. True only between
/// the two calls, and what stops the panel reporting a stamp that cannot yet be drawn.
/// </param>
public sealed record StampView(bool Configured, bool Svg, bool AwaitingRender, StampOptions Options);

/// <summary>
/// The cabinet's stamp: the image, where the number sits on it, and a page to check it against.
///
/// <para><b>The preview is the feature.</b> Lining a number up with the dotted line on somebody
/// else's logo is not something anyone gets right from three percentages typed into a form. So
/// Avocado stamps a blank page with a plausible number and hands it back, and she nudges until it
/// sits where it should. Without that, the settings below are a guessing game with a fifteen-pièce
/// batch as the feedback loop.</para>
/// </summary>
public static class ManageStamp
{
    public static IEndpointRouteBuilder MapStamp(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/settings/stamp").WithTags("Settings");

        group.MapGet("/", GetAsync);
        group.MapPut("/", SetImageAsync);
        group.MapPut("/options", SetOptionsAsync);
        group.MapDelete("/", RemoveAsync);
        group.MapGet("/image", ImageAsync);
        group.MapGet("/svg", SvgAsync);
        group.MapPut("/rendered", SetRenderedAsync);
        group.MapGet("/preview.pdf", PreviewAsync);

        return routes;
    }

    private static async Task<IResult> GetAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        var drawable = await CabinetStamp.ReferenceAsync(database, cancellationToken) is not null;
        var svg = await CabinetStamp.SvgAsync(database, cancellationToken) is not null;

        return Results.Ok(new StampView(
            drawable,
            svg,
            svg && !drawable,
            await CabinetStamp.OptionsAsync(database, cancellationToken)));
    }

    /// <summary>
    /// Reads the image she picked and keeps it in the coffre.
    ///
    /// <para>By path, not by upload. The renderer is sandboxed and has no filesystem, so the picker
    /// hands back a path and the backend is what opens it, which is the same arrangement every other
    /// file in Avocado goes through.</para>
    /// </summary>
    private static async Task<IResult> SetImageAsync(
        StampImageInput input,
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(input.Path))
        {
            return Problem("Ce fichier n'existe pas.");
        }

        if (!CabinetStamp.IsAnImage(input.Path))
        {
            return Problem($"Le tampon doit être une image : {string.Join(", ", CabinetStamp.Extensions)}.");
        }

        try
        {
            if (CabinetStamp.IsSvg(input.Path))
            {
                var svg = await File.ReadAllTextAsync(input.Path, cancellationToken).ConfigureAwait(false);

                if (!svg.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                {
                    return Problem("Ce fichier ne contient pas de dessin SVG.");
                }

                await UpsertAsync(database, PracticeSettingKeys.StampSvg, svg, cancellationToken);

                // The drawing that goes on a pièce comes back from Réglages in a moment, and until
                // it does there is nothing to stamp with. Dropping the old one is the point: keeping
                // it would stamp her new dossiers with her previous logo and say nothing.
                await database.PracticeSettings
                    .Where(setting => setting.Key == PracticeSettingKeys.StampImage)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                await database.SaveChangesAsync(cancellationToken);

                return Results.NoContent();
            }

            var source = File.OpenRead(input.Path);
            Vault.Blobs.BlobReference blob;

            await using (source.ConfigureAwait(false))
            {
                blob = await vaults.Get(Guid.Empty).Blobs.PutAsync(source, cancellationToken);
            }

            await UpsertAsync(
                database, PracticeSettingKeys.StampImage, $"{blob.Sha256}:{blob.SizeBytes}", cancellationToken);

            // A plain image places its number by hand, so an SVG left over from before would claim
            // a placeholder this stamp does not have.
            await database.PracticeSettings
                .Where(setting => setting.Key == PracticeSettingKeys.StampSvg)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            await database.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Problem($"Ce fichier n'a pas pu être lu : {exception.Message}");
        }
    }

    /// <summary>Her SVG as she gave it, for Réglages to render and to read the placeholder out of.</summary>
    private static async Task<IResult> SvgAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        var svg = await CabinetStamp.SvgAsync(database, cancellationToken);

        return svg is null ? Results.NotFound() : Results.Text(svg, "image/svg+xml");
    }

    /// <summary>
    /// The drawing Réglages made from her SVG, which is what actually goes onto a pièce.
    ///
    /// <para>Bytes rather than a path, because this file never existed on her disk: the window drew
    /// it. The one place in Avocado where the renderer hands the backend content instead of the
    /// other way round, so the size is capped and the bytes are checked to be a PNG.</para>
    /// </summary>
    private static async Task<IResult> SetRenderedAsync(
        HttpRequest request,
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        const int mostAStampCouldNeed = 8 * 1024 * 1024;

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length is 0 or > mostAStampCouldNeed)
        {
            return Problem("Le rendu du tampon est vide ou beaucoup trop lourd.");
        }

        var bytes = buffer.ToArray();

        if (!bytes.AsSpan(0, Math.Min(8, bytes.Length)).SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Problem("Le rendu du tampon n'est pas une image PNG.");
        }

        buffer.Position = 0;
        var blob = await vaults.Get(Guid.Empty).Blobs.PutAsync(buffer, cancellationToken);

        await UpsertAsync(
            database, PracticeSettingKeys.StampImage, $"{blob.Sha256}:{blob.SizeBytes}", cancellationToken);

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> SetOptionsAsync(
        StampOptions input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.WidthPoints is <= 0 or > 500 || input.MarginPoints is < 0 or > 200)
        {
            return Problem("La taille et la marge du tampon doivent tenir sur une page.");
        }

        await UpsertAsync(
            database, PracticeSettingKeys.StampOptions, CabinetStamp.Serialise(input), cancellationToken);

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> RemoveAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        // The blob stays. It is content-addressed and may be in a snapshot already, and sweeping it
        // is the blob sweep's job, not this one's.
        await database.PracticeSettings
            .Where(setting =>
                setting.Key == PracticeSettingKeys.StampImage
                || setting.Key == PracticeSettingKeys.StampSvg)
            .ExecuteDeleteAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ImageAsync(
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        var bytes = await CabinetStamp.ReadAsync(database, vaults, cancellationToken);

        return bytes is null ? Results.NotFound() : Results.File(bytes, "image/png");
    }

    /// <summary>A blank A4 with the stamp on it, numbered 12, so she can see where the number lands.</summary>
    private static async Task<IResult> PreviewAsync(
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        var stamp = await CabinetStamp.ReadAsync(database, vaults, cancellationToken);

        if (stamp is null)
        {
            return Problem("Aucun tampon n'a encore été choisi.");
        }

        var options = await CabinetStamp.OptionsAsync(database, cancellationToken);

        using var blank = new PdfDocument();
        blank.AddPage();

        using var buffer = new MemoryStream();
        blank.Save(buffer, closeStream: false);

        try
        {
            return Results.File(
                PieceStamp.Apply(buffer.ToArray(), stamp, 12, options),
                "application/pdf",
                "apercu-tampon.pdf");
        }
        catch (Exception exception)
        {
            return Problem($"Le tampon n'a pas pu être appliqué : {exception.Message}");
        }
    }

    private static async Task UpsertAsync(
        AvocadoDbContext database,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var existing = await database.PracticeSettings
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken);

        if (existing is null)
        {
            database.PracticeSettings.Add(new PracticeSetting { Key = key, Value = value });
            return;
        }

        existing.Value = value;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static IResult Problem(string detail) =>
        Results.Problem(title: "Tampon", detail: detail, statusCode: StatusCodes.Status400BadRequest);
}
