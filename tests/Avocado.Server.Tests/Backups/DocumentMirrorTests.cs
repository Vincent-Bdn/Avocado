using Avocado.Server.Features.Backups.Infrastructure;
using Avocado.Server.Features.Matters;
using Avocado.Vault.Backups;

namespace Avocado.Server.Tests.Backups;

/// <summary>
/// The dossiers' documents, copied onto a destination as ordinary files.
///
/// <para>Two properties carry the whole feature and both are about what it does <b>not</b> do: it
/// never removes anything from the destination, and a dossier whose folder is not on this machine is
/// reported rather than acted upon. A lawyer's laptop holds what is current and the NAS holds
/// everything, so « absent locally » is the normal state of an old dossier.</para>
/// </summary>
public class DocumentMirrorTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"avocado-mirror-{Guid.NewGuid():N}")).FullName;

    private string Sub(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static void Write(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private IBackupSink SinkAt(string folder) =>
        new DirectorySink(new FixedPathLocator(folder, "NAS"));

    private static TestVault WithMatter(string reference, string name, string? folder)
    {
        var vault = new TestVault();

        vault.Save(new Matter
        {
            Reference = reference,
            Name = name,
            DocumentsFolder = folder,
            OpenedOn = new DateOnly(2026, 1, 5),
        });

        return vault;
    }

    [Fact]
    public void NamesTheFolderByReferenceSoRenamingHersDoesNotStrandACopy()
    {
        Assert.Equal("Dossiers/700770-COULEYRE - EDF ENR", DocumentMirror.FolderFor("700770-COULEYRE", "EDF ENR"));

        // A colon is legal on a Mac and refused by Windows, and this name is written on both.
        Assert.DoesNotContain(':', DocumentMirror.FolderFor("23/04567", "Sté X : avenants"));
    }

    [Fact]
    public async Task CopiesTheDossiersFilesKeepingTheirShape()
    {
        var documents = Sub("dossier-couleyre");
        Write(Path.Combine(documents, "Assignation.pdf"), "a");
        Write(Path.Combine(documents, "Pièces", "Pièce 1 - Photos.pdf"), "b");

        using var vault = WithMatter("700770-COULEYRE", "EDF ENR", documents);
        var nas = Sub("nas");

        var report = await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default);

        Assert.Equal(1, report.Dossiers);
        Assert.Equal(2, report.Copied);
        Assert.Empty(report.Missing);

        var landed = Path.Combine(nas, "Dossiers", "700770-COULEYRE - EDF ENR");
        Assert.True(File.Exists(Path.Combine(landed, "Assignation.pdf")));
        Assert.True(File.Exists(Path.Combine(landed, "Pièces", "Pièce 1 - Photos.pdf")));
    }

    /// <summary>Only what changed goes over the wire, which on a NAS is the difference that matters.</summary>
    [Fact]
    public async Task SendsNothingTwice()
    {
        var documents = Sub("dossier");
        Write(Path.Combine(documents, "Conclusions.pdf"), "a");

        using var vault = WithMatter("A-1", "Client", documents);
        var nas = Sub("nas");

        Assert.Equal(1, (await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default)).Copied);
        Assert.Equal(0, (await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default)).Copied);

        Write(Path.Combine(documents, "Conclusions.pdf"), "a changed, and longer");

        Assert.Equal(1, (await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default)).Copied);
    }

    /// <summary>
    /// The one this feature exists for. She keeps the current dossiers on the laptop and the rest on
    /// the NAS; a dossier she has archived locally must not lose its copy up there.
    /// </summary>
    [Fact]
    public async Task LeavesTheDestinationAloneWhenTheLocalFolderHasGone()
    {
        var documents = Sub("dossier-archive");
        Write(Path.Combine(documents, "Vieux contrat.pdf"), "a");

        using var vault = WithMatter("B-2", "Ancien client", documents);
        var nas = Sub("nas");

        await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default);

        var landed = Path.Combine(nas, "Dossiers", "B-2 - Ancien client", "Vieux contrat.pdf");
        Assert.True(File.Exists(landed));

        // She clears it off the laptop for room.
        Directory.Delete(documents, recursive: true);

        var report = await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default);

        Assert.True(File.Exists(landed), "The copy on the NAS is the only one left and must survive.");
        Assert.Equal(0, report.Dossiers);

        // Named, so Réglages can offer to detach it rather than deciding by itself.
        var missing = Assert.Single(report.Missing);
        Assert.Equal("B-2", missing.Dossier);
        Assert.Equal(documents, missing.Folder);
    }

    /// <summary>
    /// Deleting a single file locally does not delete it on the destination either. The mirror is one
    /// direction and has no removal path at all, which is what makes an old dossier safe up there.
    /// </summary>
    [Fact]
    public async Task NeverRemovesAFileFromTheDestination()
    {
        var documents = Sub("dossier");
        Write(Path.Combine(documents, "Gardé.pdf"), "a");
        Write(Path.Combine(documents, "Supprimé plus tard.pdf"), "b");

        using var vault = WithMatter("C-3", "Client", documents);
        var nas = Sub("nas");

        await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default);
        File.Delete(Path.Combine(documents, "Supprimé plus tard.pdf"));
        await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default);

        var landed = Path.Combine(nas, "Dossiers", "C-3 - Client");
        Assert.True(File.Exists(Path.Combine(landed, "Gardé.pdf")));
        Assert.True(File.Exists(Path.Combine(landed, "Supprimé plus tard.pdf")));
    }

    /// <summary>A dossier with no folder is not a problem to report: most dossiers never get one.</summary>
    [Fact]
    public async Task IgnoresADossierThatNeverHadAFolder()
    {
        using var vault = WithMatter("D-4", "Client", null);

        var report = await DocumentMirror.RunAsync(SinkAt(Sub("nas")), vault.Database, default);

        Assert.Equal(0, report.Dossiers);
        Assert.Empty(report.Missing);
    }

    /// <summary>What the operating system leaves lying about is not a pièce.</summary>
    [Fact]
    public async Task SkipsTheNoiseTheSystemLeavesBehind()
    {
        var documents = Sub("dossier");
        Write(Path.Combine(documents, "Vrai document.pdf"), "a");
        Write(Path.Combine(documents, ".DS_Store"), "x");
        Write(Path.Combine(documents, "~$Conclusions.docx"), "x");
        Write(Path.Combine(documents, ".git", "config"), "x");

        using var vault = WithMatter("E-5", "Client", documents);
        var nas = Sub("nas");

        var report = await DocumentMirror.RunAsync(SinkAt(nas), vault.Database, default);

        Assert.Equal(1, report.Copied);
        Assert.True(File.Exists(Path.Combine(nas, "Dossiers", "E-5 - Client", "Vrai document.pdf")));
    }
}
