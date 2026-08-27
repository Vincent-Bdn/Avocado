using Avocado.Server.Features.Imports;
using Avocado.Server.Features.Imports.Infrastructure;

namespace Avocado.Server.Tests.Imports;

/// <summary>
/// Finding the dossiers in somebody's filing. Every case here is one that occurs in the real export,
/// which is the only reason to believe the rule generalises at all.
///
/// <para>The suggestion is now a starting point rather than a verdict, so most of these check what the
/// screen arrives marked with. The ones at the bottom check what happens when she disagrees.</para>
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

    /// <summary>What the screen would arrive marked with, nobody having said anything yet.</summary>
    private IReadOnlyList<ImportCandidate> Scan()
    {
        var plan = DossierScan.Read(_root);

        return DossierScan.Candidates(plan);
    }

    private string At(string relative) =>
        Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

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

        Assert.True(Assert.Single(DossierScan.Candidates(DossierScan.Read(_root, ["classes"]))).IsOpen);
        Assert.False(Assert.Single(DossierScan.Candidates(DossierScan.Read(_root, ["archives 2019"]))).IsOpen);
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
    public void TheRootItselfIsNeverSuggested()
    {
        File_("EN COURS/VATEL/a.pdf");
        System.IO.File.WriteAllText(Path.Combine(_root, "lisez-moi.txt"), "x");

        var plan = DossierScan.Read(_root);

        Assert.False(Assert.Single(plan.Folders).Suggested);
        Assert.Equal("VATEL", Assert.Single(DossierScan.Candidates(plan)).Name);

        // And the readme lying beside it is reported rather than quietly left out.
        Assert.Equal(1, DossierScan.Orphans(plan, DossierScan.Candidates(plan)));
    }

    /// <summary>Pointing at one dossier to import just that one, which the old scan could not do.</summary>
    [Fact]
    public void TheRootCanBeMarkedLikeAnyOtherFolder()
    {
        File_("Tcom/Conclusions adv/b.pdf");
        System.IO.File.WriteAllText(Path.Combine(_root, "note.pdf"), "x");

        var plan = DossierScan.Read(_root);
        var dossier = Assert.Single(DossierScan.Candidates(plan, [_root]));

        Assert.Equal(2, dossier.Files);
        Assert.Equal(0, DossierScan.Orphans(plan, [dossier]));
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

    /// <summary>
    /// CHANTERACOISE, and the reason the scan stopped being the last word.
    ///
    /// <para>It keeps CA, MED and Tcom. None of those reads as filing, so the scan walks past and
    /// offers the leaves: « Assignation et nos conclusions » with three documents, « Conclusions adv »
    /// with two. They are one affaire, and nothing about the shape of the tree says so.</para>
    /// </summary>
    [Fact]
    public void SuggestsTheLeavesWhereNothingLooksLikeFiling()
    {
        File_("CLASSES/CHANTERACOISE/Tcom/Assignation et nos conclusions/a.pdf");
        File_("CLASSES/CHANTERACOISE/Tcom/Conclusions adv/b.pdf");
        File_("CLASSES/CHANTERACOISE/MED/c.pdf");

        Assert.Equal(
            ["Assignation et nos conclusions", "Conclusions adv", "MED"],
            Scan().Select(dossier => dossier.Name).Order().ToList());
    }

    /// <summary>And she says no, the dossier is CHANTERACOISE, which takes everything below it.</summary>
    [Fact]
    public void MarkingAFolderTakesEverythingUnderneath()
    {
        File_("CLASSES/CHANTERACOISE/Tcom/Assignation et nos conclusions/a.pdf");
        File_("CLASSES/CHANTERACOISE/Tcom/Conclusions adv/b.pdf");
        File_("CLASSES/CHANTERACOISE/MED/c.pdf");

        var plan = DossierScan.Read(_root);
        var dossier = Assert.Single(DossierScan.Candidates(plan, [At("CLASSES/CHANTERACOISE")]));

        Assert.Equal("CHANTERACOISE", dossier.Name);
        Assert.Equal(3, dossier.Files);
        Assert.False(dossier.IsOpen);
    }

    /// <summary>
    /// The files sitting loose in a folder the scan walked past.
    ///
    /// <para>Six of them in CHANTERACOISE, one in Tcom, 81 across the real export, and every one was
    /// imported nowhere and reported nowhere. A count that says so is the whole fix; marking the
    /// folder above is what she does about it.</para>
    /// </summary>
    [Fact]
    public void CountsTheFilesNoChosenDossierWouldTake()
    {
        File_("CLASSES/CHANTERACOISE/loose one.pdf");
        File_("CLASSES/CHANTERACOISE/loose two.pdf");
        File_("CLASSES/CHANTERACOISE/Tcom/Conclusions adv/b.pdf");

        var plan = DossierScan.Read(_root);

        Assert.Equal(2, DossierScan.Orphans(plan, DossierScan.Candidates(plan)));
        Assert.Equal(0, DossierScan.Orphans(plan, DossierScan.Candidates(plan, [At("CLASSES/CHANTERACOISE")])));
    }

    /// <summary>
    /// A dossier inside a dossier would import the same files twice, and a duplicate is the kind of
    /// thing found a year later. The parent wins, since marking it is what said the child belongs to
    /// it.
    /// </summary>
    [Fact]
    public void DropsAMarkThatSitsInsideAnotherMark()
    {
        File_("CLASSES/CHANTERACOISE/Tcom/Conclusions adv/b.pdf");

        var plan = DossierScan.Read(_root);

        var dossier = Assert.Single(DossierScan.Candidates(
            plan,
            [At("CLASSES/CHANTERACOISE"), At("CLASSES/CHANTERACOISE/Tcom/Conclusions adv")]));

        Assert.Equal("CHANTERACOISE", dossier.Name);
        Assert.Equal(1, dossier.Files);
    }

    /// <summary>
    /// Marking nothing is not the same as never having chosen. Running the suggestions because she had
    /// emptied the list would import a hundred dossiers she had just said no to.
    /// </summary>
    [Fact]
    public void MarkingNothingImportsNothing()
    {
        File_("EN COURS/VATEL/a.pdf");

        var plan = DossierScan.Read(_root);

        Assert.Single(DossierScan.Candidates(plan, chosen: null));
        Assert.Empty(DossierScan.Candidates(plan, []));
    }

    /// <summary>
    /// The whole tree is returned, containers included, because she cannot mark a folder the screen
    /// never showed her. Counts carry up so a container says what marking it would take.
    /// </summary>
    [Fact]
    public void ReturnsTheContainersTooWithTheirTotals()
    {
        File_("CLASSES/CHANTERACOISE/Tcom/Assignation et nos conclusions/a.pdf");
        File_("CLASSES/CHANTERACOISE/Tcom/Conclusions adv/b.pdf");
        File_("CLASSES/CHANTERACOISE/loose.pdf");

        // The folder she pointed at is the first row, and CLASSES sits under it.
        var top = Assert.Single(DossierScan.Read(_root).Folders);
        var classes = Assert.Single(top.Children);
        var chanteracoise = Assert.Single(classes.Children);
        var tcom = Assert.Single(chanteracoise.Children);

        Assert.Equal("CLASSES", classes.Name);
        Assert.Equal(3, classes.TotalFiles);

        Assert.Equal(1, chanteracoise.Files);
        Assert.Equal(3, chanteracoise.TotalFiles);
        Assert.False(chanteracoise.Suggested);

        Assert.Equal(0, tcom.Files);
        Assert.Equal(2, tcom.TotalFiles);
    }

    /// <summary>She has hundreds of empty folders and none of them is a row worth reading.</summary>
    [Fact]
    public void LeavesOutFoldersThatHoldNothingAnywhere()
    {
        File_("EN COURS/VATEL/a.pdf");
        Directory.CreateDirectory(At("EN COURS/VATEL/Vide"));
        Directory.CreateDirectory(At("EN COURS/Rien du tout/Vide aussi"));

        var section = Assert.Single(Assert.Single(DossierScan.Read(_root).Folders).Children);

        Assert.Equal("EN COURS", section.Name);
        Assert.Equal("VATEL", Assert.Single(section.Children).Name);
        Assert.Empty(Assert.Single(section.Children).Children);
    }

    /// <summary>
    /// The contacts list sits in a folder beside the dossier, so marking the dossier has to find it
    /// however deep it was filed.
    /// </summary>
    [Fact]
    public void CarriesTheGestisoftExportsUpToWhicheverFolderIsMarked()
    {
        File_("CLASSES/CHANTERACOISE/Tcom/Contacts et factu/contacts.PDF");
        File_("CLASSES/CHANTERACOISE/Tcom/Contacts et factu/export.xlsx");
        File_("CLASSES/CHANTERACOISE/loose.pdf");

        var plan = DossierScan.Read(_root);
        var dossier = Assert.Single(DossierScan.Candidates(plan, [At("CLASSES/CHANTERACOISE")]));

        Assert.EndsWith("contacts.PDF", dossier.ContactsFile);
        Assert.EndsWith("export.xlsx", dossier.BillingFile);
    }

    /// <summary>
    /// « Sous CLASSES veut dire clôturé » is her filing habit, right almost always and wrong
    /// sometimes, so it is a button rather than a fact.
    /// </summary>
    [Fact]
    public void SheCanSayADossierIsStillOpenWhateverThePathSays()
    {
        File_("CLASSES/BERTANI/a.pdf");
        File_("EN COURS/VATEL/b.pdf");

        var plan = DossierScan.Read(_root);
        var marks = new[] { At("CLASSES/BERTANI"), At("EN COURS/VATEL") };

        var byThePath = DossierScan.Candidates(plan, marks);

        Assert.False(byThePath.Single(d => d.Name == "BERTANI").IsOpen);
        Assert.True(byThePath.Single(d => d.Name == "VATEL").IsOpen);

        var corrected = DossierScan.Candidates(plan, marks, new ImportChoices(
            Open: [At("CLASSES/BERTANI")],
            Closed: [At("EN COURS/VATEL")]));

        Assert.True(corrected.Single(d => d.Name == "BERTANI").IsOpen);
        Assert.False(corrected.Single(d => d.Name == "VATEL").IsOpen);
    }

    /// <summary>
    /// COULEYRE holds « Contacts couleyre.PDF » and a letter called « pas de contact connu chez EDF
    /// OA ». Both are PDFs with contact in the name, so both are offered and the better-named one is
    /// taken until she says otherwise.
    /// </summary>
    [Fact]
    public void OffersEveryPlausibleContactsListAndTakesTheBestNamed()
    {
        File_("EN COURS/COULEYRE/Contacts et factu/Contacts couleyre.PDF");
        File_("EN COURS/COULEYRE/01 Courriers/pas de contact connu chez EDF OA.pdf");

        var plan = DossierScan.Read(_root);
        var folder = Assert.Single(Assert.Single(Assert.Single(plan.Folders).Children).Children);

        Assert.Equal(2, folder.ContactsCandidates.Count);
        Assert.EndsWith("Contacts couleyre.PDF", folder.ContactsFile);

        var corrected = Assert.Single(DossierScan.Candidates(
            plan,
            [folder.Path],
            new ImportChoices(Contacts: new Dictionary<string, string>
            {
                [folder.Path] = folder.ContactsCandidates.Last(),
            })));

        Assert.EndsWith("pas de contact connu chez EDF OA.pdf", corrected.ContactsFile);
    }

    /// <summary>
    /// Everything filed in « Contacts et factu » used to be offered as a contacts list, so the real
    /// one came back behind eleven invoices. A name has to say so.
    /// </summary>
    [Fact]
    public void DoesNotOfferEveryFactureFiledBesideTheContactsList()
    {
        File_("EN COURS/COULEYRE/Contacts et factu/contacts.PDF");
        File_("EN COURS/COULEYRE/Contacts et factu/Facture CVS Detaillee n° 202615624.pdf");
        File_("EN COURS/COULEYRE/Contacts et factu/Avoir n° 202511472.pdf");

        var folder = Assert.Single(Assert.Single(Assert.Single(DossierScan.Read(_root).Folders).Children).Children);

        Assert.EndsWith("contacts.PDF", Assert.Single(folder.ContactsCandidates));
    }

    /// <summary>
    /// Clearing a pick means she looked and there is none. Falling back to the guess would hand her
    /// back the file she had just rejected.
    /// </summary>
    [Fact]
    public void ClearingAPickLeavesItEmptyRatherThanRestoringTheGuess()
    {
        File_("EN COURS/COULEYRE/Contacts et factu/contacts.PDF");
        File_("EN COURS/COULEYRE/Contacts et factu/export.xlsx");

        var plan = DossierScan.Read(_root);
        var path = At("EN COURS/COULEYRE");

        var cleared = Assert.Single(DossierScan.Candidates(plan, [path], new ImportChoices(
            Contacts: new Dictionary<string, string> { [path] = string.Empty })));

        Assert.Null(cleared.ContactsFile);

        // And what she said nothing about is still whatever the scan found.
        Assert.EndsWith("export.xlsx", cleared.BillingFile);
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
