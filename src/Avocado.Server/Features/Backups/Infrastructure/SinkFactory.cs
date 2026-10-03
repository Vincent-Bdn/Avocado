using Avocado.Vault.Backups;

namespace Avocado.Server.Features.Backups.Infrastructure;

/// <summary>
/// Turns a configured <see cref="BackupDestination"/> into something that can read and write.
///
/// <para>The whole point of the split: adding a NAS, an S3 bucket or a native Drive client is a case
/// in this switch and a class beside it, and nothing in the scheduler, the mirror or the restore path
/// changes. An unknown kind returns null rather than throwing, so a vault written by a newer version
/// degrades to "one destination I do not understand" instead of a server that will not start.</para>
/// </summary>
public sealed class SinkFactory
{
    public IBackupSink? Create(BackupDestination destination) => destination.Kind switch
    {
        BackupDestinationKinds.Folder when !string.IsNullOrWhiteSpace(destination.Path) =>
            new DirectorySink(Within(new FixedPathLocator(destination.Path, destination.Label), destination)),

        BackupDestinationKinds.Volume when destination.VolumeId is { } volumeId =>
            new DirectorySink(Within(new MarkedVolumeLocator(volumeId, destination.Label), destination)),

        _ => null,
    };

    /// <summary>Narrowed to her subfolder when she gave one, which a shared NAS always wants.</summary>
    private static ISinkLocator Within(ISinkLocator locator, BackupDestination destination) =>
        SubfolderLocator.Clean(destination.Subpath) is { Length: > 0 } subpath
            ? new SubfolderLocator(locator, subpath)
            : locator;

    /// <summary>Why a destination could not be built, for the row in Réglages.</summary>
    public string? ExplainMissing(BackupDestination destination) => destination.Kind switch
    {
        BackupDestinationKinds.Folder => "Aucun dossier n'est configuré pour cette destination.",
        BackupDestinationKinds.Volume => "Ce support n'a pas d'identifiant : reconfigurez-le.",
        "googleDrive" =>
            "Avocado ne se connecte pas directement à Google Drive. Installez « Google Drive pour " +
            "ordinateur », puis choisissez son dossier comme destination : la copie s'y fait comme " +
            "dans n'importe quel dossier, et Google s'occupe de l'envoyer.",
        _ => $"Type de destination inconnu ({destination.Kind}).",
    };
}
