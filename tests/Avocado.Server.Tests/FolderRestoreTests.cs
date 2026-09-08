using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Server.Features.Matters;
using Avocado.Vault.Blobs;
using Avocado.Vault.Crypto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avocado.Server.Tests;

/// <summary>
/// A whole practice out and back again.
///
/// <para>These go through the real capture and the real blob store rather than a fixture, because
/// the thing worth knowing is not that the restore writes files: it is that the bytes it writes are
/// the bytes she filed, after they have been deflated, encrypted, stored, and pulled back through
/// every layer between. A restore that returns subtly different files is worse than one that
/// fails.</para>
/// </summary>
public sealed class FolderRestoreTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly SecretKey _key = SecretKey.Generate();
    private readonly List<string> _temporary = [];

    private readonly EncryptedBlobStore _blobs;
    private readonly FolderCapture _capture;
    private readonly FolderRestore _restore;

    public FolderRestoreTests()
    {
        _blobs = new EncryptedBlobStore(Temp("blobs"), _key);
        _capture = new FolderCapture(new CaptureProgress(), TimeProvider.System, NullLogger<FolderCapture>.Instance);
        _restore = new FolderRestore(_blobs, NullLogger<FolderRestore>.Instance);
    }

    [Fact]
    public async Task GivesBackEveryFileByteForByte()
    {
        var source = Temp("durand");
        Dossier("2026-0001", "Durand contre Martin", source, open: true);

        var files = new Dictionary<string, string>
        {
            ["Assignation.docx"] = "Par-devant le tribunal judiciaire de Lyon",
            ["Pièces/Pièce 1 - Contrat.pdf"] = "%PDF-1.7 binaire-ish",
            ["Pièces/Pièce 2 - Mise en demeure.pdf"] = new string('x', 300_000),
            ["Correspondance/2026/Courriel.msg"] = "De: adverse@avocat.fr\nObjet: votre client",
        };

        foreach (var (relative, content) in files)
        {
            Write(source, relative, content);
        }

        await _capture.RunAsync(_blobs, _vault.Database, default);

        var into = Temp("restauré-en-cours");
        var outcome = await _restore.RunAsync(_vault.Database, into, Temp("restauré-clôturés"), null, default);

        Assert.Equal(4, outcome.Files);
        Assert.Empty(outcome.Issues);

        var folder = Path.Combine(into, "durand");

        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(path), $"{relative} did not come back.");
            Assert.Equal(content, await File.ReadAllTextAsync(path));
        }
    }

    [Fact]
    public async Task SortsDossiersIntoEnCoursAndClôturés()
    {
        var ongoing = Temp("durand");
        var closed = Temp("ancien");

        Dossier("2026-0001", "Durand", ongoing, open: true);
        Dossier("2024-0007", "Affaire finie", closed, open: false);

        Write(ongoing, "Note.docx", "En cours");
        Write(closed, "Jugement.pdf", "Définitif");

        await _capture.RunAsync(_blobs, _vault.Database, default);

        var intoOngoing = Temp("Dossiers en cours");
        var intoClosed = Temp("Dossiers clôturés");

        await _restore.RunAsync(_vault.Database, intoOngoing, intoClosed, null, default);

        Assert.True(File.Exists(Path.Combine(intoOngoing, "durand", "Note.docx")));
        Assert.True(File.Exists(Path.Combine(intoClosed, "ancien", "Jugement.pdf")));
    }

    [Fact]
    public async Task PointsEachDossierAtWhereItsDocumentsNowAre()
    {
        var source = Temp("durand");
        var matter = Dossier("2026-0001", "Durand", source, open: true);
        Write(source, "Note.docx", "Contenu");

        await _capture.RunAsync(_blobs, _vault.Database, default);

        var into = Temp("nouveau");
        await _restore.RunAsync(_vault.Database, into, into, null, default);

        // Without this the Documents tab would go on naming a folder on the machine that was lost.
        var reloaded = await _vault.Database.Matters.AsNoTracking().SingleAsync(m => m.Id == matter.Id);
        Assert.Equal(Path.Combine(into, "durand"), reloaded.DocumentsFolder);
        Assert.True(File.Exists(Path.Combine(into, "durand", "Note.docx")));
    }

    [Fact]
    public async Task KeepsTwoDossiersApartWhenTheirFoldersHadTheSameName()
    {
        // Two clients called Durand, filed in « Durand » under two different parent folders. Restored
        // side by side into one root, the second must not land inside the first.
        var first = Path.Combine(Temp("2024"), "Durand");
        var second = Path.Combine(Temp("2026"), "Durand");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        Dossier("2024-0001", "Durand Pierre", first, open: true);
        Dossier("2026-0001", "Durand Sophie", second, open: true);

        await File.WriteAllTextAsync(Path.Combine(first, "Ancien.docx"), "Pierre");
        await File.WriteAllTextAsync(Path.Combine(second, "Récent.docx"), "Sophie");

        await _capture.RunAsync(_blobs, _vault.Database, default);

        var into = Temp("tout");
        var outcome = await _restore.RunAsync(_vault.Database, into, into, null, default);

        Assert.Equal(2, outcome.Dossiers.Count);
        Assert.Equal(2, outcome.Dossiers.Select(dossier => dossier.Folder).Distinct().Count());
        Assert.True(File.Exists(Path.Combine(into, "Durand", "Ancien.docx")));
        Assert.True(File.Exists(Path.Combine(into, "Durand (2026-0001)", "Récent.docx")));
    }

    [Fact]
    public async Task NeverWritesOverSomethingAlreadyThere()
    {
        var source = Temp("durand");
        Dossier("2026-0001", "Durand", source, open: true);
        Write(source, "Conclusions.docx", "La version sauvegardée");

        await _capture.RunAsync(_blobs, _vault.Database, default);

        // She restored into a folder that already holds work of the same name. Whatever that file is,
        // it is not ours to destroy.
        var into = Temp("nouveau");
        var folder = Path.Combine(into, "durand");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Conclusions.docx"), "Ce que j'ai écrit ce matin");

        await _restore.RunAsync(_vault.Database, into, into, null, default);

        Assert.Equal("Ce que j'ai écrit ce matin", await File.ReadAllTextAsync(Path.Combine(folder, "Conclusions.docx")));
        Assert.Equal("La version sauvegardée", await File.ReadAllTextAsync(Path.Combine(folder, "Conclusions (restauré).docx")));
    }

    [Fact]
    public async Task CanBeRunTwiceWithoutDoublingEverything()
    {
        var source = Temp("durand");
        Dossier("2026-0001", "Durand", source, open: true);
        Write(source, "Note.docx", "Contenu");
        Write(source, "Pièces/Pièce 1.pdf", "%PDF");

        await _capture.RunAsync(_blobs, _vault.Database, default);

        var into = Temp("nouveau");
        await _restore.RunAsync(_vault.Database, into, into, null, default);
        await _restore.RunAsync(_vault.Database, into, into, null, default);

        // An interrupted restore is re-run by whoever is having the bad day. Doing so must converge,
        // not produce « Note (restauré).docx » beside an identical « Note.docx ».
        Assert.Equal(2, Directory.GetFiles(Path.Combine(into, "durand"), "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task ReportsADocumentTheDestinationNeverReceivedRatherThanStoppingDead()
    {
        var source = Temp("durand");
        Dossier("2026-0001", "Durand", source, open: true);
        Write(source, "Présent.docx", "Bien arrivé");
        Write(source, "Manquant.docx", "Perdu en route");

        await _capture.RunAsync(_blobs, _vault.Database, default);

        // A blob that never made it onto the USB key, or one lost from it since. The rest of the
        // practice must still come back, and the missing file must be named.
        var missing = await _vault.Database.CapturedFiles.SingleAsync(file => file.RelativePath == "Manquant.docx");
        Assert.True(_blobs.Delete(new BlobReference(missing.BlobSha256, missing.SizeBytes)));

        var into = Temp("nouveau");
        var outcome = await _restore.RunAsync(_vault.Database, into, into, null, default);

        Assert.Equal(1, outcome.Files);
        Assert.Single(outcome.Issues);
        Assert.Equal("Manquant.docx", outcome.Issues[0].Path);
        Assert.True(File.Exists(Path.Combine(into, "durand", "Présent.docx")));
    }

    [Fact]
    public async Task RefusesAManifestPathThatClimbsOutOfTheFolder()
    {
        var source = Temp("durand");
        var matter = Dossier("2026-0001", "Durand", source, open: true);
        Write(source, "Note.docx", "Contenu");

        await _capture.RunAsync(_blobs, _vault.Database, default);

        // The manifest arrives inside a database file that came off a USB key. Nothing says the key
        // was only ever written by us, and a restore runs with her full permissions.
        var row = await _vault.Database.CapturedFiles.SingleAsync();
        row.RelativePath = "../../../Windows/System32/evil.dll";
        await _vault.Database.SaveChangesAsync();

        var into = Temp("nouveau");
        var outcome = await _restore.RunAsync(_vault.Database, into, into, null, default);

        Assert.Equal(0, outcome.Files);
        Assert.Single(outcome.Issues);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(into)!, "Windows", "System32", "evil.dll")));
        Assert.NotNull(matter.Reference);
    }

    [Fact]
    public async Task LeavesTheRestoreFoldersEmptyWhenNothingWasEverCaptured()
    {
        Dossier("2026-0001", "Durand", Temp("durand"), open: true);

        var into = Temp("nouveau");
        var outcome = await _restore.RunAsync(_vault.Database, into, into, null, default);

        Assert.Empty(outcome.Dossiers);
        Assert.Empty(Directory.GetFileSystemEntries(into));
    }

    private Matter Dossier(string reference, string name, string folder, bool open) => _vault.Save(new Matter
    {
        Reference = reference,
        Name = name,
        OpenedOn = new DateOnly(2026, 1, 5),
        ClosedOn = open ? null : new DateOnly(2026, 2, 1),
        DocumentsFolder = folder,
    });

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Temp(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "avocado-restore-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(path);
        _temporary.Add(Path.GetDirectoryName(path)!);

        return path;
    }

    public void Dispose()
    {
        _vault.Dispose();
        _key.Dispose();

        foreach (var directory in _temporary)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
