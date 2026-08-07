namespace Avocado.Server.Features.Documents.Workspace;

/// <summary>
/// Where working copies live while a document is open in Word.
///
/// <para><b>Not inside the coffre.</b> It was, and that was wrong on three counts. The coffre is the
/// thing that gets backed up, and a half-saved draft has no business in a backup. The coffre may one
/// day be a network share or a remote store, and a file being edited has to be on the machine doing
/// the editing. And deleting the coffre folder is a catastrophe you recover from with the recovery
/// key, while deleting this one costs at most the last few seconds of typing, two things with
/// completely different consequences should not share a parent.</para>
///
/// <para><b>Where instead.</b> The shell passes the platform's own per-user application-state folder:
/// <c>%LOCALAPPDATA%</c> on Windows, <c>~/Library/Application Support</c> on macOS,
/// <c>~/.config</c> on Linux. Deliberately not Documents, Documents is exactly the folder OneDrive
/// and Dropbox synchronise, and putting plaintext drafts there would undo the refusal the setup
/// wizard makes such a point of.</para>
///
/// <para>It is derived rather than chosen. A machine-local scratch folder is not a decision a lawyer
/// has any basis to make, so the wizard does not ask; Réglages shows where it is.</para>
/// </summary>
public sealed class WorkingDirectory(string root)
{
    public string Root { get; } = root;

    public static WorkingDirectory Resolve(IConfiguration configuration)
    {
        var configured =
            configuration["workingDirectory"]
            ?? Environment.GetEnvironmentVariable("AVOCADO_WORKING_DIR");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new WorkingDirectory(Path.GetFullPath(configured));
        }

        // Only reached when the backend is run on its own, without the shell, a test, or a
        // developer with a terminal. ApplicationData is the same idea as what the shell passes.
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Avocado",
            "working");

        return new WorkingDirectory(fallback);
    }

    /// <summary>
    /// One folder per vault, so a future multi-vault build cannot let two of them collide, and one
    /// folder per document inside it: two dossiers both holding « conclusions.docx » must not land on
    /// the same path.
    /// </summary>
    public string For(Guid vaultId) => Path.Combine(Root, vaultId.ToString("N"));
}
