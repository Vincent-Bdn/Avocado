using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avocado.Vault.Backups;

/// <summary>
/// The little file Avocado leaves at the root of a backup destination so it can recognise it again.
///
/// <para>This exists because a removable drive has no stable address. The key that was E:\ this
/// morning is F:\ after someone plugs in a phone, and /Volumes/SANS TITRE is whatever the last person
/// named it. Trusting the letter means eventually writing a cabinet's backups onto a client's USB
/// stick, or worse, quietly not writing them at all because E:\ is now something else.</para>
///
/// <para>So the identity travels with the volume rather than with the machine. The id is generated
/// once, when the destination is set up, and matching it is the whole of detection. The rest of the
/// file is there for the human who finds the key in a drawer in three years: it is plain JSON and it
/// says what this is.</para>
///
/// <para>It holds no key material and nothing about the practice. It is a name tag.</para>
/// </summary>
public sealed class SinkMarker
{
    [JsonPropertyName("sinkId")]
    public Guid SinkId { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>For whoever opens this file wondering what it is doing on their USB key.</summary>
    [JsonPropertyName("readme")]
    public string Readme { get; init; } =
        "Cette clé est une destination de sauvegarde Avocado. Le dossier « avocado » contient des " +
        "sauvegardes chiffrées, illisibles sans la clé de récupération du cabinet. Ne supprimez ce " +
        "fichier que si vous ne voulez plus que ce support serve aux sauvegardes.";

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SinkMarker Write(string root, string label)
    {
        var marker = new SinkMarker
        {
            SinkId = Guid.CreateVersion7(),
            Label = label,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, BackupLayout.MarkerFile), JsonSerializer.Serialize(marker, Format));

        return marker;
    }

    /// <summary>Reads the marker at a folder root. Null if there is none, or it is not ours to read.</summary>
    public static SinkMarker? Read(string root)
    {
        var path = Path.Combine(root, BackupLayout.MarkerFile);

        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SinkMarker>(File.ReadAllText(path))
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A destination we cannot read the marker of is a destination we do not recognise. That is
            // the same answer as "not here", and it is the safe one.
            return null;
        }
    }
}
