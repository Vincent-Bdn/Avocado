using Avocado.Server.Features.Backups;
using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Server.Features.Matters;
using Avocado.Vault.Blobs;
using Avocado.Vault.Crypto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avocado.Server.Tests;

/// <summary>
/// The half of a backup system that is written last and tested never. It is tested here, because the
/// day it matters is the day the laptop is gone, and every one of these is a way it could quietly
/// have been holding nothing.
/// </summary>
public sealed class FolderCaptureTests : IDisposable
{
    private readonly TestVault _vault = new();
    private readonly string _blobRoot = Path.Combine(Path.GetTempPath(), "avocado-capture-" + Guid.NewGuid().ToString("N"));
    private readonly string _dossierRoot = Path.Combine(Path.GetTempPath(), "avocado-dossier-" + Guid.NewGuid().ToString("N"));
    private readonly SecretKey _key = SecretKey.Generate();
    private readonly EncryptedBlobStore _blobs;
    private readonly FolderCapture _capture;

    public FolderCaptureTests()
    {
        _blobs = new EncryptedBlobStore(_blobRoot, _key);
        _capture = new FolderCapture(new CaptureProgress(), TimeProvider.System, NullLogger<FolderCapture>.Instance);
        Directory.CreateDirectory(_dossierRoot);
    }

    [Fact]
    public async Task CarriesEveryFileInTheDossierWithThePathItWasFiledUnder()
    {
        var matter = Dossier("2026-0001", _dossierRoot);
        Write("Assignation.docx", "Par-devant le tribunal");
        Write("Pièces/Pièce 1 - Contrat.pdf", "%PDF-1.7");
        Write("Correspondance/2026/Courriel du 3 mars.msg", "De: adverse@avocat.fr");

        var report = await _capture.RunAsync(_blobs, _vault.Database, default);

        Assert.Equal(3, report.Files);
        Assert.Equal(3, report.Added);
        Assert.Empty(report.Issues);

        var paths = await _vault.Database.CapturedFiles
            .Where(file => file.MatterId == matter.Id)
            .Select(file => file.RelativePath)
            .OrderBy(path => path)
            .ToListAsync();

        // Forward slashes whatever the machine wrote them with: the restore may be onto a Mac.
        Assert.Equal(
            ["Assignation.docx", "Correspondance/2026/Courriel du 3 mars.msg", "Pièces/Pièce 1 - Contrat.pdf"],
            paths);
    }

    [Fact]
    public async Task ReadsNothingASecondTimeWhenNothingChanged()
    {
        Dossier("2026-0001", _dossierRoot);
        Write("Assignation.docx", "Par-devant le tribunal");
        Write("Pièces/Pièce 1.pdf", "%PDF-1.7");

        await _capture.RunAsync(_blobs, _vault.Database, default);
        var second = await _capture.RunAsync(_blobs, _vault.Database, default);

        // The whole reason a nightly pass over twelve gigabytes is affordable at all.
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Dropped);
        Assert.Equal(2, second.Files);
    }

    [Fact]
    public async Task PicksUpAFileSheEditedToday()
    {
        Dossier("2026-0001", _dossierRoot);
        var path = Write("Conclusions.docx", "Première version");

        await _capture.RunAsync(_blobs, _vault.Database, default);
        var before = await _vault.Database.CapturedFiles.AsNoTracking().SingleAsync();

        await File.WriteAllTextAsync(path, "Deuxième version, bien plus longue");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        var report = await _capture.RunAsync(_blobs, _vault.Database, default);
        var after = await _vault.Database.CapturedFiles.AsNoTracking().SingleAsync();

        Assert.Equal(1, report.Added);
        Assert.Equal(1, report.Files);          // Replaced, not duplicated.
        Assert.NotEqual(before.BlobSha256, after.BlobSha256);

        // The old blob is still on disk. That is what makes last week's snapshot restorable, and it
        // is why deleting blobs is a sweep against every retained snapshot rather than a delete here.
        Assert.True(_blobs.Exists(new BlobReference(before.BlobSha256, before.SizeBytes)));
    }

    [Fact]
    public async Task ForgetsAFileSheDeleted()
    {
        Dossier("2026-0001", _dossierRoot);
        Write("Note.docx", "Brouillon");
        var doomed = Write("Erreur.docx", "Mauvais dossier");

        await _capture.RunAsync(_blobs, _vault.Database, default);
        File.Delete(doomed);
        var report = await _capture.RunAsync(_blobs, _vault.Database, default);

        Assert.Equal(1, report.Dropped);
        Assert.Equal(1, report.Files);
        Assert.Equal("Note.docx", await _vault.Database.CapturedFiles.Select(f => f.RelativePath).SingleAsync());
    }

    /// <summary>
    /// The one that would have been a disaster. Half the beta practices keep dossiers on an external
    /// disk or a network share, and a folder that is not there tonight looks exactly like a folder
    /// whose every file was deleted. Treating the two the same would empty the sauvegarde of that
    /// dossier on the one night nobody was watching, and the blobs would be swept away behind it.
    /// </summary>
    [Fact]
    public async Task KeepsWhatItHasWhenTheDiskIsUnplugged()
    {
        Dossier("2026-0001", _dossierRoot);
        Write("Assignation.docx", "Par-devant le tribunal");
        Write("Pièces/Pièce 1.pdf", "%PDF-1.7");

        await _capture.RunAsync(_blobs, _vault.Database, default);

        Directory.Delete(_dossierRoot, recursive: true);
        var report = await _capture.RunAsync(_blobs, _vault.Database, default);

        Assert.Equal(2, report.Files);
        Assert.Equal(0, report.Dropped);
        Assert.Equal(1, report.Unreachable);
        Assert.Contains(report.Issues, issue => issue.Dossier == "2026-0001");
    }

    [Fact]
    public async Task SaysSoRatherThanSkippingAFileItCannotRead()
    {
        Dossier("2026-0001", _dossierRoot);
        Write("Lisible.docx", "Bien");
        var locked = Write("Verrouillé.docx", "Mal");

        // Held exclusively, which is not what Word does but is what some scanners and sync clients
        // do. The capture opens shared, so this is the case it genuinely cannot get past.
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = await _capture.RunAsync(_blobs, _vault.Database, default);

            Assert.Equal(1, report.Added);
            Assert.Equal(1, report.IssueCount);
            Assert.Contains(report.Issues, issue => issue.Path == "Verrouillé.docx");
        }

        // And it is picked up on the next pass, once whatever held it has let go.
        var after = await _capture.RunAsync(_blobs, _vault.Database, default);
        Assert.Equal(2, after.Files);
        Assert.Empty(after.Issues);
    }

    [Fact]
    public async Task ReadsADocumentSheHasOpenInWord()
    {
        Dossier("2026-0001", _dossierRoot);
        var open = Write("Conclusions.docx", "En cours de rédaction");

        // How Word actually holds a document: others may read it. Refusing these would mean the
        // files she is working on this week are the ones never backed up.
        using var word = new FileStream(open, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        var report = await _capture.RunAsync(_blobs, _vault.Database, default);

        Assert.Equal(1, report.Files);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task LeavesWordsLockFilesAndTheSystemsLitterAlone()
    {
        Dossier("2026-0001", _dossierRoot);
        Write("Conclusions.docx", "Vrai document");
        Write("~$nclusions.docx", "marie");
        Write("Thumbs.db", "cache");
        Write("Pièces/.DS_Store", "cache");

        var report = await _capture.RunAsync(_blobs, _vault.Database, default);

        Assert.Equal(1, report.Files);
        Assert.Equal("Conclusions.docx", await _vault.Database.CapturedFiles.Select(f => f.RelativePath).SingleAsync());
    }

    [Fact]
    public async Task StoresOneCopyOfAPieceFiledInTwoDossiers()
    {
        var second = Path.Combine(Path.GetTempPath(), "avocado-dossier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(second);

        try
        {
            Dossier("2026-0001", _dossierRoot);
            Dossier("2026-0002", second);

            var contract = "Contrat de cession du 4 avril, vingt pages.";
            Write("Pièces/Contrat.pdf", contract);
            await File.WriteAllTextAsync(Path.Combine(second, "Contrat.pdf"), contract);

            var report = await _capture.RunAsync(_blobs, _vault.Database, default);

            Assert.Equal(2, report.Files);
            Assert.Single(await _vault.Database.CapturedFiles.Select(f => f.BlobSha256).Distinct().ToListAsync());
            Assert.Single(Directory.EnumerateFiles(_blobRoot, "*.blob", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public async Task IgnoresADossierSheHasNotPointedAtAFolder()
    {
        _vault.Save(new Matter
        {
            Reference = "2026-0009",
            Name = "Conseil ponctuel",
            OpenedOn = new DateOnly(2026, 1, 5),
            DocumentsFolder = null,
        });

        var report = await _capture.RunAsync(_blobs, _vault.Database, default);

        Assert.Equal(0, report.Dossiers);
        Assert.Equal(0, report.Files);
        Assert.Empty(report.Issues);
    }

    private Matter Dossier(string reference, string folder) => _vault.Save(new Matter
    {
        Reference = reference,
        Name = "Durand contre Martin",
        OpenedOn = new DateOnly(2026, 1, 5),
        DocumentsFolder = folder,
    });

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_dossierRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }

    public void Dispose()
    {
        _vault.Dispose();
        _key.Dispose();

        foreach (var directory in new[] { _blobRoot, _dossierRoot })
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
