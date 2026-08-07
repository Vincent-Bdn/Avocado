using Avocado.Vault;
using Avocado.Vault.Keys;
using Avocado.Vault.Storage;

// Deliberately dependency-free and separate from the web host: this has to work on the day the app
// does not. If Avocado will not start, `avocado unlock` and `avocado backup` are the difference
// between an annoying evening and a destroyed practice.

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    return args[0] switch
    {
        "create" => Create(Argument(args, 1, "folder")),
        "info" => Info(Argument(args, 1, "folder")),
        "unlock" => Unlock(Argument(args, 1, "folder")),
        "backup" => Backup(Argument(args, 1, "folder")),
        "verify-recovery" => VerifyRecovery(Argument(args, 1, "folder")),
        "-h" or "--help" or "help" => PrintUsage(),
        _ => Fail($"Unknown command '{args[0]}'."),
    };
}
catch (VaultException exception)
{
    return Fail(exception.Message);
}

static int Create(string folder)
{
    var creation = VaultManager.Create(folder);
    using var vault = creation.Vault;

    Console.WriteLine($"Vault created at {vault.Paths.Root}");
    Console.WriteLine($"  id            {vault.Id}");
    Console.WriteLine($"  unlock paths  {string.Join(", ", vault.Keyring.Keys.Select(k => k.Label))}");
    Console.WriteLine();
    Console.WriteLine("RECOVERY KEY - shown once, and never again:");
    Console.WriteLine();
    Console.WriteLine($"    {creation.RecoveryCode}");
    Console.WriteLine();
    Console.WriteLine("Print it or copy it to a USB key now. Without it, a backup taken from this");
    Console.WriteLine("machine cannot be restored anywhere else.");
    return 0;
}

static int Info(string folder)
{
    var paths = new VaultPaths(folder);
    var keyring = VaultManager.InspectKeyring(folder);

    Console.WriteLine($"Vault {keyring.VaultId}");
    Console.WriteLine($"  folder     {paths.Root}");
    Console.WriteLine($"  database   {(File.Exists(paths.DatabaseFile) ? $"{new FileInfo(paths.DatabaseFile).Length:N0} bytes" : "missing")}");
    Console.WriteLine($"  encrypted  {(VaultDatabase.LooksEncrypted(paths.DatabaseFile) ? "yes" : "NO - this is a bug, report it")}");
    Console.WriteLine($"  documents  {CountBlobs(paths):N0}");
    Console.WriteLine($"  backups    {CountBackups(paths):N0}");
    Console.WriteLine("  unlock paths:");

    foreach (var key in keyring.Keys.OrderBy(k => k.CreatedAt))
    {
        Console.WriteLine($"    {key.Kind,-10} {key.Label,-40} added {key.CreatedAt:yyyy-MM-dd}");
    }

    if (!keyring.HasRecoveryKey)
    {
        Console.WriteLine();
        Console.WriteLine("  WARNING: no recovery key. Backups from this vault cannot be restored elsewhere.");
    }

    return 0;
}

static int Unlock(string folder)
{
    using var vault = Open(folder);
    Console.WriteLine($"Unlocked vault {vault.Id}.");
    return 0;
}

static int Backup(string folder)
{
    using var vault = Open(folder);
    var path = vault.CreateBackup("manual");

    Console.WriteLine($"Backup written to {path} ({new FileInfo(path).Length:N0} bytes, encrypted).");
    Console.WriteLine("Restoring it on another machine needs the recovery key.");
    return 0;
}

static int VerifyRecovery(string folder)
{
    using var vault = Open(folder);

    Console.Write("Recovery key: ");
    var code = Console.ReadLine();

    if (vault.VerifyRecoveryCode(code ?? string.Empty))
    {
        Console.WriteLine("This recovery key opens the vault. Put it back where you found it.");
        return 0;
    }

    Console.Error.WriteLine("This recovery key does NOT open the vault.");
    Console.Error.WriteLine("Run 'avocado info' to check one is enrolled, then regenerate from the app.");
    return 1;
}

/// <summary>Device key first, falling back to a typed recovery key, the same order the app uses.</summary>
static OpenVault Open(string folder)
{
    try
    {
        return VaultManager.UnlockWithDeviceKey(folder);
    }
    catch (VaultException) when (VaultManager.InspectKeyring(folder).HasRecoveryKey)
    {
        Console.Error.WriteLine("This machine cannot unlock the vault on its own.");
        Console.Write("Recovery key: ");
        return VaultManager.UnlockWithRecoveryCode(folder, Console.ReadLine() ?? string.Empty);
    }
}

static long CountBlobs(VaultPaths paths) =>
    Directory.Exists(paths.BlobsDirectory)
        ? Directory.EnumerateFiles(paths.BlobsDirectory, "*.blob", SearchOption.AllDirectories).LongCount()
        : 0;

static long CountBackups(VaultPaths paths) =>
    Directory.Exists(paths.BackupsDirectory)
        ? Directory.EnumerateFiles(paths.BackupsDirectory, "*.db").LongCount()
        : 0;

static string Argument(string[] args, int index, string name) =>
    index < args.Length ? args[index] : throw new VaultException($"Missing <{name}>. Run 'avocado help'.");

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 1;
}

static int PrintUsage()
{
    Console.WriteLine("""
        avocado - vault maintenance for Avocado

          avocado create <folder>           Create a vault and print its recovery key
          avocado info <folder>             Show the vault's unlock paths and health
          avocado unlock <folder>           Check the vault opens on this machine
          avocado backup <folder>           Write an encrypted snapshot to backups/
          avocado verify-recovery <folder>  Check a printed recovery key still works

        This tool talks to the vault directly and needs no running Avocado.
        """);
    return 0;
}
