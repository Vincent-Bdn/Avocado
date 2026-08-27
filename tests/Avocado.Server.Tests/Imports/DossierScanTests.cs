using Avocado.Server.Features.Imports;
using Avocado.Server.Features.Imports.Infrastructure;

namespace Avocado.Server.Tests.Imports;

/// <summary>
/// Finding the dossiers in somebody's filing. Every case here is one that occurs in the real export,
/// which is the only reason to believe the rule generalises at all.
/// </summary>
public class DossierScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"avocado-scan-{Guid.NewGuid():N}");

    public DossierScanTests() => Directory.CreateDirectory(_root);

    private void File_(string relative)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "x");
    }

    private IReadOnlyList<ImportCandidate> Scan() => DossierScan.Read(_root).Candidates;

    /// <summary>A client folder holding only files is one dossier. Forty of hers look like this.</summary>
    [Fact]
    public void AFlatFolderIsOneDossier()
    {
        File_("EN COURS/ARISTOPHIL/note.pdf");
        File_("EN COURS/ARISTOPHIL/arret.pdf");

        var dossier = Assert.Single(Scan());

        Assert.Equal("ARISTOPHIL", dossier.Name);
        Assert.Equal(2, dossier.Files);
    }

    /// <summary>
    /// PHILEAS LOUNGE. Numbered subfolders are one dossier's filing, not four affaires, and getting
    /// this wrong scatters a matter across its own drawers.
    /// </summary>
    [Fact]
    public void NumberedSubfoldersAreFilingAndNotAffaires()
    {
        File_("EN COURS/PHILEAS LOUNGE/01 Courriers/a.pdf");
        File_("EN COURS/PHILEAS LOUNGE/02 Procédure TC MONTPELLIER/b.pdf");
        File_("EN COURS/PHILEAS LOUNGE/07 Facturation/c.pdf");

        var dossier = Assert.Single(Scan());

        Assert.Equal("PHILEAS LOUNGE", dossier.Name);
        Assert.Equal(3, dossier.Files);
    }

    /// <summary>ANODEA. Named subfolders with their own filing inside are separate matters.</summary>
    [Fact]
    public void NamedSubfoldersWithTheirOwnFilingAreSeparateDossiers()
    {
        File_("EN COURS/ANODEA/CIM du MAIL c CH ROMORANTHIN/Emails/a.msg");
        File_("EN COURS/ANODEA/TMF c SILLAND/01 Actes/b.pdf");

        var dossiers = Scan();

        Assert.Equal(2, dossiers.Count);
        Assert.All(dossiers, dossier => Assert.Equal("ANODEA", dossier.Client));
        Assert.Contains(dossiers, dossier => dossier.Name == "TMF c SILLAND");
    }

    /// <summary>COULEYRE. The dossier is Gestisoft's number, and the client is the folder above it.</summary>
    [Fact]
    public void TakesTheClientFromTheFolderAboveAndKeepsTheGestisoftCode()
    {
        File_("EN COURS/COULEYRE/700770/05 Facturation/facture.pdf");

        var dossier = Assert.Single(Scan());

        Assert.Equal("700770", dossier.Name);
        Assert.Equal("COULEYRE", dossier.Client);
        Assert.Equal("700770", dossier.GestisoftCode);
    }

    /// <summary>
    /// Archived is read off the path, so it works wherever the dossier sits under CLASSES rather than
    /// only one level down.
    /// </summary>
    [Fact]
    public void AnythingUnderClassesIsClosed()
    {
        File_("EN COURS/VATEL/a.pdf");
        File_("CLASSES/BERTANI/b.pdf");
        File_("CLASSES/JH TRANSPORT/700119/01 Courriers/c.pdf");

        var dossiers = Scan();

        Assert.True(dossiers.Single(d => d.Name == "VATEL").IsOpen);
        Assert.False(dossiers.Single(d => d.Name == "BERTANI").IsOpen);
        Assert.False(dossiers.Single(d => d.Name == "700119").IsOpen);
    }

    /// <summary>The word is hers, not a standard, so it is a setting and not a constant.</summary>
    [Fact]
    public void TheArchivedWordCanBeChanged()
    {
        File_("ARCHIVES 2019/DUPONT/a.pdf");

        Assert.True(Assert.Single(DossierScan.Read(_root, ["classes"]).Candidates).IsOpen);
        Assert.False(Assert.Single(DossierScan.Read(_root, ["archives 2019"]).Candidates).IsOpen);
    }

    /// <summary>
    /// The client folder standing directly under CLASSES is its own client: naming it « CLASSES »
    /// would put every closed matter under one imaginary client.
    /// </summary>
    [Fact]
    public void ADossierDirectlyUnderTheStatusFolderIsItsOwnClient()
    {
        File_("CLASSES/BERTANI/b.pdf");

        Assert.Equal("BERTANI", Assert.Single(Scan()).Client);
    }

    /// <summary>
    /// She filed them under « Contacts et factu » and « Contact et factu », and named them
    /// contacts.PDF, contact.PDF and « Contacts couleyre.PDF ». Matching folder names would have found
    /// five of the seven that exist.
    /// </summary>
    [Fact]
    public void FindsTheGestisoftExportsByWhatTheyAreRatherThanWhereTheySit()
    {
        File_("EN COURS/COULEYRE/700770/01 Courriers/a.pdf");
        File_("EN COURS/COULEYRE/700770/Contacts et factu/Contacts couleyre.PDF");
        File_("EN COURS/COULEYRE/700770/Contacts et factu/excel factu.xlsx");

        var dossier = Assert.Single(Scan());

        Assert.EndsWith("Contacts couleyre.PDF", dossier.ContactsFile);
        Assert.EndsWith("excel factu.xlsx", dossier.BillingFile);
    }

    /// <summary>Ninety-seven of her hundred and four have neither, and that is not a failure.</summary>
    [Fact]
    public void ADossierWithoutTheExportsIsStillADossier()
    {
        File_("EN COURS/VATEL/a.pdf");

        var dossier = Assert.Single(Scan());

        Assert.Null(dossier.ContactsFile);
        Assert.Null(dossier.BillingFile);
    }

    /// <summary>
    /// Pointing at a whole archive must not import it as one matter holding everything, which is what
    /// a root full of loose files would otherwise look like.
    /// </summary>
    [Fact]
    public void TheRootItselfIsNeverADossier()
    {
        File_("EN COURS/VATEL/a.pdf");
        System.IO.File.WriteAllText(Path.Combine(_root, "lisez-moi.txt"), "x");

        Assert.Single(Scan());
    }

    /// <summary>
    /// A client called « 2 RIDE », which exists. While one leading digit counted as numbering, that
    /// single folder made all of CLASSES read as one dossier holding fifty-nine clients and thirteen
    /// thousand files. Drawers are numbered 01, 02, 03; clients are not.
    /// </summary>
    [Fact]
    public void AClientWhoseNameStartsWithADigitIsNotAFilingFolder()
    {
        File_("CLASSES/2 RIDE/a.pdf");
        File_("CLASSES/BERTANI/b.pdf");
        File_("CLASSES/ARISTOPHIL/c.pdf");

        var dossiers = Scan();

        Assert.Equal(3, dossiers.Count);
        Assert.Contains(dossiers, dossier => dossier.Name == "2 RIDE");
    }

    /// <summary>
    /// « Vivre Greffé » keeps Docs client, Droit de la santé, Gestion and Modeles données perso. Only
    /// some of those are words anyone would list in advance, so one recognised filing folder has to be
    /// enough: demanding a majority broke it into six matters.
    /// </summary>
    [Fact]
    public void OneRecognisedFilingFolderIsEnough()
    {
        File_("EN COURS/Vivre Greffé/Docs client/a.pdf");
        File_("EN COURS/Vivre Greffé/Droit de la santé/b.pdf");
        File_("EN COURS/Vivre Greffé/Gestion/c.pdf");
        File_("EN COURS/Vivre Greffé/Modeles données perso/d.pdf");

        var dossier = Assert.Single(Scan());

        Assert.Equal("Vivre Greffé", dossier.Name);
        Assert.Equal(4, dossier.Files);
    }

    [Fact]
    public void CountsEmailsSeparately()
    {
        File_("EN COURS/VATEL/a.pdf");
        File_("EN COURS/VATEL/b.msg");
        File_("EN COURS/VATEL/c.eml");

        var dossier = Assert.Single(Scan());

        Assert.Equal(3, dossier.Files);
        Assert.Equal(2, dossier.Emails);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
