using Avocado.Server.Data;
using Avocado.Server.Features.Contacts;
using Avocado.Server.Features.Contacts.Enums;
using Avocado.Server.Features.Documents;
using Avocado.Server.Features.Mails.Infrastructure;
using Avocado.Server.Features.Matters;
using Avocado.Vault;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <param name="Done">Files written so far, across every dossier.</param>
/// <param name="Current">The dossier being imported, for the line on screen.</param>
public sealed record ImportProgress(
    int Dossiers,
    int DossiersDone,
    int Files,
    int Done,
    int Emails,
    long Bytes,
    string? Current,
    bool Finished,
    string? Error,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Writes a scanned plan into the vault: a contact and a dossier per candidate, a document per file,
/// and a journal entry per email.
///
/// <para>Runs in the background and reports progress, because the real export is thirteen thousand
/// files and thirteen gigabytes. Encrypting that takes a while, and a request that simply does not
/// answer for twenty minutes is indistinguishable from one that has failed.</para>
///
/// <para><b>Nothing here invents data.</b> The client is a contact named after the folder, which is
/// the only name the export contains. Billing is left empty rather than filled with plausible
/// figures: an invented total is worse than an absent one, since it looks like a fact. The hourly rate
/// comes from Réglages, as it would for a dossier created by hand.</para>
/// </summary>
public sealed class GestisoftImporter(
    IVaultStore vaults,
    VaultDbContextFactory contexts,
    MailIngest mails,
    ILogger<GestisoftImporter> logger)
{
    private readonly object _gate = new();
    private ImportProgress? _progress;

    /// <summary>Null until an import has been started. Read by the screen every second or so.</summary>
    public ImportProgress? Progress
    {
        get { lock (_gate) { return _progress; } }
        private set { lock (_gate) { _progress = value; } }
    }

    public bool IsRunning => Progress is { Finished: false };

    public async Task RunAsync(IReadOnlyList<ImportCandidate> candidates, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        Progress = new ImportProgress(
            candidates.Count, 0,
            candidates.Sum(candidate => candidate.Files), 0, 0,
            candidates.Sum(candidate => candidate.Bytes),
            null, false, null, warnings);

        try
        {
            var vault = vaults.Get(Guid.Empty);
            var done = 0;
            var emails = 0;
            var index = 0;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Progress = Progress! with { Current = candidate.Name, DossiersDone = index };

                var (files, mailsFiled) = await ImportOneAsync(vault, candidate, warnings, cancellationToken)
                    .ConfigureAwait(false);

                done += files;
                emails += mailsFiled;
                index++;

                Progress = Progress! with { Done = done, Emails = emails, DossiersDone = index };
            }

            Progress = Progress! with { Finished = true, Current = null };
            logger.LogInformation("Import finished: {Dossiers} dossiers, {Files} documents.", index, done);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Import failed.");
            Progress = (Progress ?? new ImportProgress(0, 0, 0, 0, 0, 0, null, false, null, warnings))
                with { Finished = true, Error = exception.Message };
        }
    }

    private async Task<(int Files, int Emails)> ImportOneAsync(
        OpenVault vault,
        ImportCandidate candidate,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        await using var database = contexts.Create(vault.Id);

        var client = await FindOrCreateClientAsync(database, candidate.Client, cancellationToken)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var matter = new Matter
        {
            Reference = await NextReferenceAsync(database, cancellationToken).ConfigureAwait(false),
            Name = candidate.Name,
            // Provisional. The real dates are inside the emails and are only known once they are read,
            // so the matter is dated again at the end of this method.
            OpenedOn = today,
            HourlyRateCents = await RateAsync(database, cancellationToken).ConfigureAwait(false),
            ClosedOn = candidate.IsOpen ? null : today,
        };

        matter.Parties.Add(new MatterParty { ContactId = client, IsClient = true, Role = "Client" });

        database.Matters.Add(matter);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var files = 0;
        var emails = 0;

        foreach (var file in Directory.EnumerateFiles(candidate.SourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsNoise(file))
            {
                continue;
            }

            try
            {
                if (await ImportFileAsync(vault, database, matter.Id, candidate.SourcePath, file, cancellationToken)
                        .ConfigureAwait(false))
                {
                    emails++;
                }

                files++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or VaultException)
            {
                // One unreadable file must not cost the other thirteen thousand. It is named and the
                // import carries on, which is the only useful behaviour at this scale.
                warnings.Add($"{Path.GetFileName(file)} : {exception.Message}");
                logger.LogWarning(exception, "Skipped {File}.", file);
            }

            // Saved in batches rather than per file: thirteen thousand round trips to SQLite is most
            // of the wall clock, and a batch that fails loses a page of work rather than everything.
            if (files % 50 == 0)
            {
                await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                Progress = Progress! with { Done = Progress.Done + 50 };
            }
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await DateFromCorrespondenceAsync(database, matter, cancellationToken).ConfigureAwait(false);

        return (files, emails);
    }

    /// <summary>
    /// Dates the dossier from the correspondence it contains.
    ///
    /// <para>The export carries no dates of its own: every file's last-write time is the moment the
    /// export ran, so taking them would date a matter from 2019 to the afternoon somebody clicked
    /// « exporter », and sort a decade of history into one day. The emails are different, they carry
    /// the date they were actually sent, and there are 4,715 of them. First and last are a far better
    /// answer to when a dossier ran than anything the filesystem knows.</para>
    ///
    /// <para>A dossier with no correspondence keeps the import date, which is at least visibly a
    /// placeholder rather than a plausible fiction.</para>
    /// </summary>
    private static async Task DateFromCorrespondenceAsync(
        AvocadoDbContext database,
        Matter matter,
        CancellationToken cancellationToken)
    {
        var dates = await database.Activities
            .Where(activity => activity.MatterId == matter.Id)
            .Select(activity => activity.OccurredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (dates.Count == 0)
        {
            return;
        }

        matter.OpenedOn = DateOnly.FromDateTime(dates.Min().UtcDateTime);

        if (matter.ClosedOn is not null)
        {
            matter.ClosedOn = DateOnly.FromDateTime(dates.Max().UtcDateTime);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when the file was an email and became a journal entry rather than a plain document.</summary>
    private async Task<bool> ImportFileAsync(
        OpenVault vault,
        AvocadoDbContext database,
        Guid matterId,
        string root,
        string file,
        CancellationToken cancellationToken)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var folder = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : null;

        var reference = await StoreAsync(vault, file, cancellationToken).ConfigureAwait(false);

        var document = new Document
        {
            MatterId = matterId,
            BlobSha256 = reference.Sha256,
            SizeBytes = reference.SizeBytes,
            FileName = Path.GetFileName(file),
            Folder = folder,
            AddedAt = Written(file),
        };

        database.Documents.Add(document);

        // The 4,710 .msg files in the real export are the reason this exists. Left as documents they
        // are opaque containers only Outlook opens; read, each becomes a dated journal entry and its
        // attachments become pièces of their own.
        var attachments = await mails.RecordAsync(database, matterId, document.Id, file, cancellationToken)
            .ConfigureAwait(false);

        if (attachments is null)
        {
            return false;
        }

        foreach (var attachment in attachments)
        {
            using var content = new MemoryStream(attachment.Content);
            var stored = await vault.Blobs.PutAsync(content, cancellationToken).ConfigureAwait(false);

            database.Documents.Add(new Document
            {
                MatterId = matterId,
                BlobSha256 = stored.Sha256,
                SizeBytes = stored.SizeBytes,
                FileName = attachment.FileName,
                MimeType = attachment.ContentType,
                Folder = folder,
                AddedAt = document.AddedAt,
            });
        }

        return true;
    }

    private static async Task<Vault.Blobs.BlobReference> StoreAsync(
        OpenVault vault,
        string file,
        CancellationToken cancellationToken)
    {
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return await vault.Blobs.PutAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<Guid> FindOrCreateClientAsync(
        AvocadoDbContext database,
        string name,
        CancellationToken cancellationToken)
    {
        // A client with two folders, one in EN COURS and one in CLASSES, is one client. Matching on
        // the name is crude and it is what the export gives us.
        var existing = await database.Contacts
            .FirstOrDefaultAsync(contact => contact.LegalName == name, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        var contact = new Contact
        {
            // Organisation rather than Individual: the export names companies, and a fiche with a
            // legal name is easier to correct than one with a surname invented from it.
            Type = ContactType.Organisation,
            LegalName = name,
        };

        database.Contacts.Add(contact);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return contact.Id;
    }

    private static async Task<string> NextReferenceAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"{year}-";

        var highest = (await database.Matters
                .Where(matter => matter.Reference.StartsWith(prefix))
                .Select(matter => matter.Reference)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(reference => int.TryParse(reference[prefix.Length..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highest + 1:D4}";
    }

    private static async Task<long> RateAsync(AvocadoDbContext database, CancellationToken cancellationToken)
    {
        var setting = await database.PracticeSettings
            .FirstOrDefaultAsync(entry => entry.Key == Settings.PracticeSettingKeys.HourlyRateCents, cancellationToken)
            .ConfigureAwait(false);

        return long.TryParse(setting?.Value, out var rate)
            ? rate
            : Settings.PracticeSettingKeys.DefaultHourlyRateCents;
    }

    /// <summary>
    /// The oldest file in the folder, which is the best available answer to when the dossier started.
    /// Gestisoft exported no dates, and today's date on a matter from 2019 would be worse than a
    /// guess: it would sort the history wrongly for ever.
    /// </summary>
    private static DateOnly OpenedOn(string path) =>
        DateOnly.FromDateTime(Stamps(path).DefaultIfEmpty(DateTime.UtcNow).Min());

    private static DateOnly ClosedOn(string path) =>
        DateOnly.FromDateTime(Stamps(path).DefaultIfEmpty(DateTime.UtcNow).Max());

    private static IEnumerable<DateTime> Stamps(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var written = Written(file).UtcDateTime;

            // Before Avocado existed, and after today, both mean a clock that cannot be trusted.
            if (written.Year is >= 1990 and <= 2100)
            {
                yield return written;
            }
        }
    }

    private static DateTimeOffset Written(string file)
    {
        try
        {
            return new FileInfo(file).LastWriteTimeUtc;
        }
        catch (IOException)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static bool IsNoise(string path)
    {
        var name = Path.GetFileName(path);

        return name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
            || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".DS_Store", StringComparison.Ordinal)
            || name.StartsWith("~$", StringComparison.Ordinal);
    }
}
