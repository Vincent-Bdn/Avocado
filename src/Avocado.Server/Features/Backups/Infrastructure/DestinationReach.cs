using Avocado.Vault.Storage;

namespace Avocado.Server.Features.Backups.Infrastructure;

/// <summary>How far from this machine a destination actually gets the data.</summary>
public enum DestinationReach
{
    /// <summary>Genuinely elsewhere: a removable drive, a share, or a folder a sync client uploads.</summary>
    OffMachine,

    /// <summary>Another folder on this computer. Survives a mistake, survives nothing else.</summary>
    SameMachine,

    /// <summary>Inside the vault it is supposed to protect. Not a destination at all.</summary>
    InsideVault,
}

/// <param name="Detail">In French, ready to show.</param>
/// <param name="SyncRoot">The sync client's folder, when that is what makes this off-machine.</param>
public sealed record ReachVerdict(DestinationReach Reach, string Detail, string? SyncRoot = null)
{
    public bool IsOffMachine => Reach is DestinationReach.OffMachine;
}

/// <summary>
/// Decides whether a folder is a backup or a second copy of the problem.
///
/// <para>This exists because the screen was telling people they were safe when they were not. A
/// destination beside the vault takes a copy, reports success, and the interface said « vous ne
/// perdriez rien ». Every word of that was true about mistakes and false about the failure backups
/// exist for: one disk dies and both copies go together. A backup system that overstates its reach is
/// worse than none, because someone stops worrying.</para>
///
/// <para>Off-machine is not the same as remote. A folder a sync client watches is off-machine even
/// though it is a local path, because something else carries it away; that is the arrangement the
/// setup wizard recommends, and it should be recognised as the good answer it is.</para>
///
/// <para>What this cannot see is a backup mechanism nobody told us about, a scheduled robocopy, a
/// Time Machine volume, a corporate agent. So SameMachine is a warning and never a refusal. Only
/// writing into the vault is refused, because there is no arrangement in which that helps.</para>
/// </summary>
public static class DestinationReachInspector
{
    public static ReachVerdict Inspect(string path, string vaultRoot)
    {
        var full = Path.GetFullPath(path);

        if (IsInside(full, Path.GetFullPath(vaultRoot)))
        {
            return new ReachVerdict(
                DestinationReach.InsideVault,
                "Ce dossier est dans le coffre lui-même. Une copie rangée à l'intérieur de ce qu'elle " +
                "protège disparaît exactement en même temps.");
        }

        // Before the drive check, and deliberately: a synced folder is usually on the internal disk,
        // and looking at the disk first would classify the wizard's own recommendation as useless.
        if (CloudSyncDetector.IsInsideSyncedFolder(full, out var syncRoot))
        {
            return new ReachVerdict(
                DestinationReach.OffMachine,
                $"Dossier synchronisé ({Path.GetFileName(syncRoot!.TrimEnd(Path.DirectorySeparatorChar))}). " +
                "Votre logiciel de synchronisation en enverra une copie hors de cet ordinateur.",
                syncRoot);
        }

        // Before DriveInfo, because DriveInfo cannot answer about one. A UNC path has no drive letter
        // and `new DriveInfo(@"\\NAS\partage")` throws, so the catch below swallowed it and every NAS
        // addressed the natural way was reported as « ce dossier est sur cet ordinateur… les deux
        // copies partiraient ensemble ». The backup was running; the screen said it was pointless.
        // A mapped drive letter was recognised correctly, which is why this went unnoticed.
        //
        // Windows only in practice, and that is correct rather than a gap: on a Mac or on Linux
        // every character of « \\NAS\partage » is legal in a file name, so GetFullPath above has
        // already turned that string into a relative local path and it is not a share. A share
        // genuinely mounted there is /Volumes/NAS/… or /mnt/nas/…, indistinguishable from any other
        // absolute path, and the DriveType.Network test below is what catches it.
        if (IsUnc(full))
        {
            return new ReachVerdict(DestinationReach.OffMachine, "Emplacement réseau.");
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(full) ?? full);

            if (drive.DriveType is DriveType.Removable)
            {
                return new ReachVerdict(DestinationReach.OffMachine, "Support amovible.");
            }

            if (drive.DriveType is DriveType.Network)
            {
                return new ReachVerdict(DestinationReach.OffMachine, "Emplacement réseau.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Unreadable drive metadata is not evidence of anything. Fall through to the warning,
            // which is the cautious answer.
        }

        return new ReachVerdict(
            DestinationReach.SameMachine,
            "Ce dossier est sur cet ordinateur. Il protège d'une fausse manœuvre, pas d'un vol, d'une " +
            "panne de disque ni d'un dégât des eaux : les deux copies partiraient ensemble.");
    }

    /// <summary>
    /// <c>\\NAS\partage</c>, and its forward-slash spelling, which Windows accepts everywhere.
    ///
    /// <para>Two separators and then something: <c>\\</c> alone is not a path, and a single leading
    /// separator is a rooted local path on the current drive.</para>
    /// </summary>
    private static bool IsUnc(string path)
    {
        if (path.Length < 3)
        {
            return false;
        }

        var first = path[0];
        var second = path[1];

        return (first is '\\' or '/')
            && (second is '\\' or '/')
            && path[2] is not ('\\' or '/');
    }

    /// <summary>
    /// Segment-aware, so <c>C:\Avocado-sauvegardes</c> is not read as being inside <c>C:\Avocado</c>.
    /// </summary>
    private static bool IsInside(string candidate, string parent)
    {
        var normalised = parent.TrimEnd(Path.DirectorySeparatorChar);

        return candidate.Equals(normalised, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(normalised + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
