using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avocado.Server.Features.Documents.Workspace;

/// <summary>
/// Where documents live in the clear while they are being worked on: a single file open in Word, or a
/// whole dossier opened as a folder.
///
/// <para><b>Not inside the coffre.</b> It was, and that was wrong on three counts. The coffre is the
/// thing that gets backed up, and a half-saved draft has no business in a backup. The coffre may one
/// day be a network share or a remote store, and a file being edited has to be on the machine doing
/// the editing. And deleting the coffre folder is a catastrophe you recover from with the recovery
/// key, while deleting this one costs at most the last few seconds of typing, two things with
/// completely different consequences should not share a parent.</para>
///
/// <para><b>Chosen, not derived, and that is a change.</b> It used to hold nothing but transient
/// working copies, so the reasoning was that a machine-local scratch folder is not a decision a lawyer
/// has any basis to make. Opening a whole dossier as a folder ended that: this is now a place she
/// navigates to in the file manager, drags messages into and works in for an afternoon. Somewhere she
/// has to find is somewhere she gets to pick, at the same level of choice as the coffre itself.</para>
///
/// <para>The choice is stored on the machine and never in the vault. The vault may be restored onto a
/// different computer, or one day live on a share, and carrying a path from the old machine would
/// point the new one at a folder that does not exist or, worse, belongs to something else.</para>
/// </summary>
public sealed class WorkingDirectory
{
    private string _root;

    public WorkingDirectory(string root) => _root = Path.GetFullPath(root);

    public string Root => _root;

    /// <summary>True when a command line or the shell fixed it, in which case Réglages cannot move it.</summary>
    public bool IsOverridden { get; private init; }

    /// <summary>The default, shown in the wizard so she is choosing rather than inventing.</summary>
    public static string Suggested => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Avocado - Dossiers ouverts");

    public static WorkingDirectory Resolve(IConfiguration configuration)
    {
        // An explicit override wins and stays fixed: it is how the tests and a developer's terminal
        // point the backend somewhere disposable, and Réglages must not fight it.
        var configured =
            configuration["workingDirectory"]
            ?? Environment.GetEnvironmentVariable("AVOCADO_WORKING_DIR");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new WorkingDirectory(configured) { IsOverridden = true };
        }

        return new WorkingDirectory(MachinePreference.Read() ?? Suggested);
    }

    /// <summary>
    /// Moves it. Nothing is copied: the caller refuses this while any dossier is open, so there is
    /// nothing in the old folder worth carrying, and moving decrypted documents around behind
    /// someone's back is not a thing to do quietly.
    /// </summary>
    public void MoveTo(string root)
    {
        _root = Path.GetFullPath(root);
        MachinePreference.Write(_root);
    }

    /// <summary>
    /// Where single documents are checked out while they are open, one folder per document: two
    /// dossiers both holding « conclusions.docx » must not land on the same path. One level per vault
    /// so a future multi-vault build cannot let two collide.
    ///
    /// <para><b>Nothing else may live under here.</b> DocumentWorkspace sweeps this folder at startup
    /// and treats every directory in it as a checked-out document, deleting any whose name is not a
    /// document id. Putting dossier folders inside it, which is what the first version did, meant an
    /// open dossier was recursively deleted as an orphan the next time the application started,
    /// taking whatever had been dropped into it. Hence <see cref="DossiersFor"/>, one level up.</para>
    /// </summary>
    /// <para>Not under <see cref="Root"/>: that folder is the one she chose and navigates to, and it
    /// should contain dossiers she recognises rather than a tree of GUIDs. This is scratch, it is
    /// never opened by hand, and it belongs in the application's own state folder.</para>
    public string For(Guid vaultId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Avocado",
        "working",
        vaultId.ToString("N"));

    /// <summary>
    /// Where whole dossiers are opened: straight into the folder she chose, one directory per dossier
    /// and nothing else.
    ///
    /// <para>It used to interpose « dossiers » and the vault id. Both were structure for its own sake:
    /// the folder is already called « Dossiers ouverts », and one practice has one coffre, so the id
    /// bought nothing and cost every path she reads a line of hexadecimal.</para>
    /// </summary>
    public string DossiersFor(Guid vaultId) => Root;
}

/// <summary>
/// The one preference that belongs to the computer rather than to the practice, kept in the platform's
/// own application-state folder so it survives moving the coffre and never travels with it.
/// </summary>
internal sealed class MachinePreference
{
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Avocado",
        "machine.json");

    public static string? Read()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<MachinePreference>(File.ReadAllText(Path))?.WorkingDirectory
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable preference is not worth refusing to start over. The default applies.
            return null;
        }
    }

    public static void Write(string workingDirectory)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        File.WriteAllText(
            Path,
            JsonSerializer.Serialize(
                new MachinePreference { WorkingDirectory = workingDirectory },
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
