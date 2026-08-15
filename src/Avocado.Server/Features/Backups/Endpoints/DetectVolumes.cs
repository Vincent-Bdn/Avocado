using Avocado.Vault.Backups;

namespace Avocado.Server.Features.Backups.Endpoints;

/// <param name="AlreadyPrepared">True when Avocado's marker is already on it, with the label it was given.</param>
public sealed record DetectedVolume(string Path, string Label, bool AlreadyPrepared, long FreeBytes);

/// <summary>
/// What is plugged in right now, so choosing a USB key is picking from a list rather than typing a
/// drive letter and hoping.
/// </summary>
public static class DetectVolumes
{
    public static Task<IResult> HandleAsync()
    {
        var volumes = new List<DetectedVolume>();

        foreach (var root in VolumeScanner.CandidateRoots())
        {
            try
            {
                var drive = new DriveInfo(root);

                // The system disk is not a backup destination, and offering it would be offering the
                // one place that does not survive the failure this exists for.
                if (drive.DriveType is not (DriveType.Removable or DriveType.Network))
                {
                    continue;
                }

                var marker = SinkMarker.Read(root);

                volumes.Add(new DetectedVolume(
                    root,
                    marker?.Label ?? (string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : drive.VolumeLabel),
                    marker is not null,
                    drive.AvailableFreeSpace));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A card reader with no card, a share whose server went away. Not offerable, not worth
                // reporting.
            }
        }

        return Task.FromResult(Results.Ok(volumes));
    }
}
