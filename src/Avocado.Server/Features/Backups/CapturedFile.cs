namespace Avocado.Server.Features.Backups;

/// <summary>
/// One file from a dossier's folder, as the sauvegarde currently holds it.
///
/// <para><b>Why the manifest is a table in the vault database.</b> Every snapshot is a copy of that
/// database, so the list of what a snapshot contains travels inside the snapshot itself, taken at the
/// same instant, by the same copy. There is no second file to keep in step and no way for the two to
/// disagree. Restoring a snapshot from March restores March's manifest, and it names blobs that were
/// on the destination in March, which is exactly the guarantee a restore needs.</para>
///
/// <para><b>Avocado still does not own these files.</b> A row here is a photograph, not a claim: the
/// file lives in her folder, she renames it, moves it and deletes it without telling anyone, and the
/// next capture simply notices. Nothing in the application reads a document through this table.</para>
/// </summary>
public class CapturedFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid MatterId { get; set; }

    /// <summary>
    /// Below the dossier's folder, with <c>/</c> separators whatever the machine uses.
    ///
    /// <para>Relative, and that is the whole of what makes a restore onto a new machine possible:
    /// <c>C:\Users\Marie\Dossiers\Durand\Pièces\Pièce 3.pdf</c> means nothing on the replacement Mac,
    /// but <c>Pièces/Pièce 3.pdf</c> means the same thing everywhere.</para>
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the file's contents, which is what names its blob.</summary>
    public string BlobSha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>
    /// The file's own last-write time when it was read. Together with the size, this is what says
    /// « unchanged » on the next pass without opening and hashing thirteen thousand files.
    /// </summary>
    public DateTimeOffset ModifiedAt { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
