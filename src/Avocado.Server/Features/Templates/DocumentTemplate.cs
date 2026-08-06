namespace Avocado.Server.Features.Templates;

/// <summary>
/// A .docx she wrote herself, with <c>{{placeholders}}</c> where the dossier's own wording belongs.
/// <para>
/// The file is stored in the encrypted blob store like any other document, because a lettre de
/// mission template contains the cabinet's letterhead, its bank details and its wording — none of
/// which belongs in clear on disk any more than a client file does.
/// </para>
/// </summary>
public class DocumentTemplate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// « Lettre de mission », « Courrier », « Convention d'honoraires ». Free text, like every other
    /// vocabulary in the application: she will invent kinds nobody thought of.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>Hex SHA-256 of the plaintext .docx — its <c>BlobReference</c>.</summary>
    public string BlobSha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string FileName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
