using Avocado.Server.Data;
using Avocado.Server.Features.Imports;
using Avocado.Server.Features.Imports.Infrastructure;
using Avocado.Server.Features.Mails.Infrastructure;
using Avocado.Vault;

namespace Avocado.Server.Hosting;

/// <summary>
/// Running an import without the window, from a terminal.
///
/// <para>The same import the Réglages screen runs, and the same code: this exists because a practice
/// with four hundred dossiers and forty gigabytes may want to start it and go home, and because a
/// migration is the kind of thing that is worth being able to repeat from a script rather than by
/// clicking. Anything the screen can do it can do; it simply says so on stdout.</para>
///
/// <para>It lives on the server executable rather than in the CLI. Avocado.Cli is deliberately kept to
/// the vault library alone, so that <c>avocado backup</c> still works on the day the application does
/// not, and importing needs EF Core, MsgReader and the whole of the Documents slice. The server binary
/// already carries all of that and already ships beside the app.</para>
///
/// <code>
/// Avocado.Server --import "D:\AVOCAT\Dossiers clients" --vault "C:\Users\me\Documents\Avocado"
/// Avocado.Server --import "D:\..." --vault "C:\..." --templates      writes the two spreadsheets
/// Avocado.Server --import "D:\..." --vault "C:\..." --split "ANODEA" --split "DLT GROUP"
/// </code>
/// </summary>
public static class ImportCommand
{
    public static bool Requested(string[] args) => args.Contains("--import", StringComparer.Ordinal);

    public static async Task<int> RunAsync(string[] args)
    {
        var root = Value(args, "--import");
        var vaultFolder = Value(args, "--vault")
            ?? Environment.GetEnvironmentVariable("AVOCADO_VAULT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Avocado");

        if (root is null || !Directory.Exists(root))
        {
            return Fail("Usage: Avocado.Server --import <dossier exporté> [--vault <coffre>] [--templates] [--split <nom>]");
        }

        var plan = GestisoftScan.Read(root);

        if (plan.Candidates.Count == 0)
        {
            return Fail($"Aucun dossier trouvé dans « {root} ». Il doit contenir « EN COURS » et « CLASSES ».");
        }

        if (args.Contains("--templates", StringComparer.Ordinal))
        {
            foreach (var file in ImportSidecars.WriteTemplates(root, plan.Candidates))
            {
                Console.WriteLine($"Écrit  {file}");
            }

            Console.WriteLine($"{plan.Candidates.Count} dossiers. Remplissez ce qui vous intéresse, puis relancez sans --templates.");
            return 0;
        }

        var split = Values(args, "--split");
        var candidates = new List<ImportCandidate>();

        foreach (var candidate in plan.Candidates)
        {
            if (split.Any(name => name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.AddRange(GestisoftScan.Split(candidate));
            }
            else
            {
                candidates.Add(candidate);
            }
        }

        var sidecars = ImportSidecars.Read(root);

        Console.WriteLine($"Coffre  {vaultFolder}");
        Console.WriteLine($"Source  {root}");
        Console.WriteLine(
            $"À importer  {candidates.Count} dossiers, {candidates.Sum(c => c.Files):N0} documents, " +
            $"{candidates.Sum(c => c.Emails):N0} courriels, {candidates.Sum(c => c.Bytes) / 1e9:N1} Go");

        if (sidecars.Tiers.Count > 0 || sidecars.Facturation.Count > 0)
        {
            Console.WriteLine($"Repris  {sidecars.Tiers.Count} tiers, {sidecars.Facturation.Count} lignes de facturation");
        }

        OpenVault vault;

        try
        {
            vault = VaultManager.UnlockWithDeviceKey(vaultFolder);
        }
        catch (VaultException exception)
        {
            // A stack trace is not an answer to « the coffre is not where you said ». The two things
            // that go wrong here are a wrong path and a vault this machine cannot open, and both have
            // a next step worth naming.
            return Fail(
                $"{exception.Message}{Environment.NewLine}" +
                $"Vérifiez --vault, ou ouvrez d'abord Avocado sur cette machine pour créer le coffre.");
        }

        using (vault)
        {
        var store = new SingleVaultStore(vault);
        var contexts = new VaultDbContextFactory(store);

        // The window does this at startup and this command does not have one. A vault created by
        // `avocado create`, or one made by an older release, has no tables until the migrations run,
        // and the import would otherwise fail on the first insert with « no such table: contacts ».
        // It snapshots first, as every migration does.
        await VaultMigrator.EnsureUpToDateAsync(
            vault, contexts, new ConsoleLogger<GestisoftImporter>()).ConfigureAwait(false);

        var importer = new GestisoftImporter(
            store,
            contexts,
            new MailIngest(new ConsoleLogger<MailIngest>()),
            new ConsoleLogger<GestisoftImporter>());

        var reporting = Task.Run(() => ReportAsync(importer));
        await importer.RunAsync(candidates, sidecars, CancellationToken.None).ConfigureAwait(false);
        await reporting.ConfigureAwait(false);

        var progress = importer.Progress!;

        Console.WriteLine();
        Console.WriteLine(
            $"Terminé  {progress.DossiersDone} dossiers, {progress.Done:N0} documents, {progress.Emails:N0} courriels");

        foreach (var warning in progress.Warnings.Take(20))
        {
            Console.WriteLine($"  ! {warning}");
        }

        if (progress.Warnings.Count > 20)
        {
            Console.WriteLine($"  ! … et {progress.Warnings.Count - 20} autres");
        }

        return progress.Error is null ? 0 : Fail(progress.Error);
        }
    }

    /// <summary>
    /// One line, rewritten in place. A terminal scrolling thirteen thousand lines tells you less than
    /// a single one that keeps moving, and it is what someone glances at from across the room.
    /// </summary>
    private static async Task ReportAsync(GestisoftImporter importer)
    {
        while (importer.Progress is not { Finished: true })
        {
            if (importer.Progress is { } progress && !Console.IsOutputRedirected)
            {
                var line =
                    $"  {progress.DossiersDone}/{progress.Dossiers} dossiers · " +
                    $"{progress.Done:N0}/{progress.Files:N0} documents · " +
                    $"{progress.Emails:N0} courriels · {progress.Current}";

                Console.Write($"\r{line.PadRight(Math.Min(Console.WindowWidth - 1, 120))[..Math.Min(line.Length, 120)]}");
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static List<string> Values(string[] args, string name)
    {
        var values = new List<string>();

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name)
            {
                values.Add(args[index + 1]);
            }
        }

        return values;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

/// <summary>The one vault this process opened, since there is no session to ask.</summary>
internal sealed class SingleVaultStore(OpenVault vault) : IVaultStore
{
    public OpenVault Get(Guid vaultId) => vault;

    public bool TryGet(Guid vaultId, out OpenVault? found)
    {
        found = vault;
        return true;
    }
}

/// <summary>Logging to a terminal, so the import's own warnings are visible without a host.</summary>
internal sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            Console.Error.WriteLine($"  {logLevel}: {formatter(state, exception)}");
        }
    }
}
