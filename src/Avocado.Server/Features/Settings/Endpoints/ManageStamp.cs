using Avocado.Server.Data;
using Avocado.Server.Features.Documents.Folders;
using Avocado.Vault;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf;

namespace Avocado.Server.Features.Settings.Endpoints;

/// <param name="Path">A file she picked, read by the backend. The renderer has no filesystem.</param>
public sealed record StampImageInput(string Path);

/// <param name="Configured">Whether a stamp image has been given at all.</param>
public sealed record StampView(bool Configured, StampOptions Options);

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
        group.MapGet("/preview.pdf", PreviewAsync);

        return routes;
    }

    private static async Task<IResult> GetAsync(AvocadoDbContext database, CancellationToken cancellationToken) =>
        Results.Ok(new StampView(
            await CabinetStamp.ReferenceAsync(database, cancellationToken) is not null,
            await CabinetStamp.OptionsAsync(database, cancellationToken)));

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
            var source = File.OpenRead(input.Path);
            Vault.Blobs.BlobReference blob;

            await using (source.ConfigureAwait(false))
            {
                blob = await vaults.Get(Guid.Empty).Blobs.PutAsync(source, cancellationToken);
            }

            await UpsertAsync(
                database, PracticeSettingKeys.StampImage, $"{blob.Sha256}:{blob.SizeBytes}", cancellationToken);

            await database.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Problem($"Ce fichier n'a pas pu être lu : {exception.Message}");
        }
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
            .Where(setting => setting.Key == PracticeSettingKeys.StampImage)
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
