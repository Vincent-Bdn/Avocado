using System.Text.Json;
using Avocado.Server.Data;
using Avocado.Server.Features.Settings;
using Avocado.Vault;
using Avocado.Vault.Blobs;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Folders;

/// <summary>
/// The cabinet's own stamp: the image, and where the number goes on it.
///
/// <para>Kept in the coffre like a modèle, because it is one: a file of hers that Avocado uses to
/// produce documents. Storing it as a blob means it is encrypted, it travels in the sauvegarde, and
/// it comes back on a new machine with everything else, which matters because it is a graphic most
/// cabinets have exactly one copy of and could not easily redraw.</para>
///
/// <para>The options are one JSON row rather than seven columns. They are a handful of numbers she
/// nudges once, when she first lines the number up with the dotted line on her own stamp, and never
/// touches again.</para>
/// </summary>
public static class CabinetStamp
{
    private static readonly JsonSerializerOptions Format = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// What she can hand over as a stamp.
    ///
    /// <para><b>SVG is here, and PDFsharp still draws rasters.</b> The drawing is done by the one
    /// piece of this application that already contains a complete SVG engine: the window. Réglages
    /// reads the placeholder's position out of the live document, takes the placeholder out, and
    /// renders what is left to a transparent PNG at a size no stamp will outgrow. What reaches the
    /// coffre is that PNG, which is why nothing downstream had to learn a second graphics stack.</para>
    /// </summary>
    public static readonly string[] Extensions = [".svg", ".png", ".jpg", ".jpeg"];

    /// <summary>Raster formats, which are stored exactly as she gave them.</summary>
    public static readonly string[] RasterExtensions = [".png", ".jpg", ".jpeg"];

    public static bool IsAnImage(string fileName) =>
        Extensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static bool IsSvg(string fileName) =>
        fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The placeholder a stamp SVG writes where the pièce number should land.
    ///
    /// <para>Spelled like the repères in the modèle de facture, because it is the same idea and she
    /// has met it once already.</para>
    /// </summary>
    public const string Placeholder = "{{numero}}";

    /// <summary>Her SVG, or null when the stamp is a plain image.</summary>
    public static async Task<string?> SvgAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var stored = await database.PracticeSettings
            .AsNoTracking()
            .Where(setting => setting.Key == PracticeSettingKeys.StampSvg)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(stored) ? null : stored;
    }

    /// <summary>The stored image, or null when she has not given Avocado one.</summary>
    public static async Task<BlobReference?> ReferenceAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var stored = await database.PracticeSettings
            .AsNoTracking()
            .Where(setting => setting.Key == PracticeSettingKeys.StampImage)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // « sha256:size ». Two fields, one row, and no second table for one image.
        if (stored?.Split(':') is [var sha, var size] && long.TryParse(size, out var bytes))
        {
            return new BlobReference(sha, bytes);
        }

        return null;
    }

    /// <summary>
    /// The stamp's bytes, or null when there is none.
    ///
    /// <para>Takes the store rather than an open vault, so that a practice with no stamp configured
    /// never resolves one. Versement has to work without a stamp, and reaching into the coffre to
    /// discover that there is nothing to reach for is how an optional feature becomes a hard
    /// dependency.</para>
    /// </summary>
    public static async Task<byte[]?> ReadAsync(
        AvocadoDbContext database,
        IVaultStore vaults,
        CancellationToken cancellationToken)
    {
        if (await ReferenceAsync(database, cancellationToken).ConfigureAwait(false) is not { } reference)
        {
            return null;
        }

        try
        {
            var vault = vaults.Get(Guid.Empty);

            await using var stream = vault.Blobs.OpenRead(reference);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            return buffer.ToArray();
        }
        catch (VaultException)
        {
            // Referenced but not there: a coffre restored without its blobs. Treated as « no stamp »
            // so that versement still works, uns­tamped, rather than failing on every pièce.
            return null;
        }
    }

    public static async Task<StampOptions> OptionsAsync(
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var stored = await database.PracticeSettings
            .AsNoTracking()
            .Where(setting => setting.Key == PracticeSettingKeys.StampOptions)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(stored))
        {
            return new StampOptions();
        }

        try
        {
            return JsonSerializer.Deserialize<StampOptions>(stored, Format) ?? new StampOptions();
        }
        catch (JsonException)
        {
            // Written by an older shape. The defaults are the ones her own tool chose anyway.
            return new StampOptions();
        }
    }

    public static string Serialise(StampOptions options) => JsonSerializer.Serialize(options, Format);
}
