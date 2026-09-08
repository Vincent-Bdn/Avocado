namespace Avocado.Server.Features.Backups.Infrastructure;

/// <param name="Dossier">Which one is being read, by reference, so the line moves visibly.</param>
public sealed record CaptureRunning(int Dossier, int Dossiers, int Files, string? Reference);

/// <summary>
/// Whether a capture is running, for the Sauvegarde screen.
///
/// <para>Its whole reason for existing is the first night. A practice being read for the first time
/// is twelve gigabytes and takes the better part of an hour, during which a screen with no indicator
/// is a screen that has hung. Afterwards it is over in seconds and nobody ever sees this.</para>
///
/// <para>One writer, the capture, and any number of readers on request threads. A record swapped
/// whole under <see cref="Volatile"/> rather than four mutable counters, so a reader can never catch
/// « dossier 7 of 3 » mid-update.</para>
/// </summary>
public sealed class CaptureProgress
{
    private CaptureRunning? _current;

    public CaptureRunning? Current => Volatile.Read(ref _current);

    public void Begin(int dossiers) =>
        Volatile.Write(ref _current, new CaptureRunning(0, dossiers, 0, null));

    public void Advance(int dossier, string reference, int files) =>
        Volatile.Write(
            ref _current,
            new CaptureRunning(dossier, Volatile.Read(ref _current)?.Dossiers ?? dossier, files, reference));

    public void End() => Volatile.Write(ref _current, null);
}
