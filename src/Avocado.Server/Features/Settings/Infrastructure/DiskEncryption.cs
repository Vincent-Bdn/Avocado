using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Avocado.Server.Features.Settings.Infrastructure;

/// <param name="State">« on », « off » or « unknown ». Never anything else, and never a guess.</param>
/// <param name="Mechanism">FileVault, BitLocker, LUKS: what to name in the sentence she reads.</param>
public sealed record DiskEncryptionStatus(string State, string Mechanism);

/// <summary>
/// Whether the disk her documents sit on is encrypted at rest.
///
/// <para><b>Why this exists.</b> Documents used to live encrypted inside the vault, which meant
/// decrypting them into a working folder, watching it, reconciling it and putting them back: two
/// thousand lines of machinery that ten lawyers found unusable and none of them asked for. Documents
/// live in her own folders now, so at-rest encryption is the operating system's job, which it does
/// invisibly and better. What is left for Avocado is to know whether it is switched on, because a
/// dossier of client files on an unencrypted laptop is a breach of secret professionnel waiting for a
/// theft.</para>
///
/// <para><b>Three states, and « unknown » is a real answer.</b> On Windows the authoritative check
/// needs elevation and Avocado does not run elevated; the registry has a hint, and a hint is not
/// enough to tell a lawyer her clients' files are safe. Being wrong in that direction is worse than
/// admitting we cannot see, so an unverified machine is told to look, and shown where.</para>
/// </summary>
public sealed class DiskEncryption
{
    private DiskEncryptionStatus? _known;

    /// <summary>
    /// Cached for the life of the process: switching FileVault on takes a restart and an hour of
    /// re-encryption, and this is read by every screen that mentions documents.
    /// </summary>
    public async Task<DiskEncryptionStatus> ReadAsync(CancellationToken cancellationToken)
    {
        return _known ??= await LookAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DiskEncryptionStatus> LookAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsMacOS())
        {
            // Authoritative, and readable by anyone: « FileVault is On. » / « FileVault is Off. »
            var said = await RunAsync("fdesetup", "status", cancellationToken).ConfigureAwait(false);

            return new DiskEncryptionStatus(
                said is null ? "unknown" : said.Contains("is On", StringComparison.OrdinalIgnoreCase) ? "on" : "off",
                "FileVault");
        }

        if (OperatingSystem.IsLinux())
        {
            return new DiskEncryptionStatus(LinuxState(), "LUKS");
        }

        // Windows. Win32_EncryptableVolume answers this properly and refuses without elevation, which
        // Avocado has no business asking for, so the honest answer is that we do not know.
        return new DiskEncryptionStatus("unknown", "BitLocker");
    }

    /// <summary>
    /// Whether the root filesystem sits on a dm-crypt device.
    ///
    /// <para>Read from sysfs rather than shelled out to lsblk, which is not installed everywhere. A
    /// dm device whose uuid begins with CRYPT- is what LUKS produces.</para>
    /// </summary>
    private static string LinuxState()
    {
        try
        {
            var crypt = Directory.EnumerateDirectories("/sys/block", "dm-*")
                .Select(device => Path.Combine(device, "dm", "uuid"))
                .Where(File.Exists)
                .Select(File.ReadAllText)
                .Any(uuid => uuid.StartsWith("CRYPT-", StringComparison.Ordinal));

            return crypt ? "on" : "off";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    /// <summary>Null when the command is missing, fails, or takes too long to be worth waiting for.</summary>
    private static async Task<string?> RunAsync(string file, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or InvalidOperationException
                or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>What the window should open when she is asked to check for herself.</summary>
    public static string SettingsPane =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "x-apple.systempreferences:com.apple.preference.security?FileVault"
            : "ms-settings:deviceencryption";
}
