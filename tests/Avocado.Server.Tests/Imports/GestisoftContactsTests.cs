using System.Text;
using Avocado.Server.Features.Imports.Infrastructure;

namespace Avocado.Server.Tests.Imports;

/// <summary>
/// « Liste des contacts », the report Gestisoft prints beside a dossier, and the only place the export
/// says who the parties are. Everything else about a dossier can be retyped in an evening; ten parties
/// each with a role, a town and an email, across a hundred dossiers, cannot.
///
/// <para>The fixtures are written here rather than checked in, both because her files are a client's
/// data and because what is being tested is the shape of the report, which is what these state.</para>
/// </summary>
public class GestisoftContactsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"avocado-contacts-{Guid.NewGuid():N}");

    public GestisoftContactsTests() => Directory.CreateDirectory(_folder);

    /// <summary>
    /// One line per literal handed to a Tj, which is how the report is printed, plus the document
    /// information dictionary that every export carries.
    /// </summary>
    private string Pdf(params string[] lines)
    {
        var content = new StringBuilder("BT /F1 9 Tf 12 TL 40 800 Td\n");

        foreach (var line in lines)
        {
            var escaped = line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            content.Append('(').Append(escaped).Append(") Tj T*\n");
        }

        content.Append("ET\n");

        var document = new StringBuilder("%PDF-1.4\n")
            .Append("1 0 obj\n<< /Producer (QuickReports PDF Export) /CreationDate (D:20260827143000) >>\nendobj\n")
            .Append("2 0 obj\n<< /Length ").Append(content.Length).Append(" >>\nstream\n")
            .Append(content)
            .Append("endstream\nendobj\n%%EOF\n");

        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, document.ToString(), Encoding.Latin1);

        return path;
    }

    /// <summary>Three spaces per level, exactly as the report indents.</summary>
    private static string At(int level, string text) => new string(' ', level * 3) + text;

    private string Report() => Pdf(
        "Liste des contacts",
        "En Date du 27/08/2026",
        "700978 - TELE MEDECINS DE FRANCE / SILLAND NANCY-ZBO-PPA-05-Commercial-Contrats--",
        At(1, "Client"),
        At(2, "TELE MEDECINS DE FRANCE      33000 BORDEAUX"),
        At(3, "05.56.01.81.70 (T)"),
        At(2, "Interlocuteur client"),
        At(3, "ORADIANSE - Mme DESRUES Laura"),
        At(4, "laura.desrues@oradianse.com (E)"),
        At(1, "Partie adverse : adversaires ou autres parties appelées"),
        At(2, "SILLAND Nancy      78560 LE PORT MARLY"),
        At(3, "Avocat de la partie adverse"),
        At(4, "SEURIN Héléne      33000 BORDEAUX"),
        At(5, "seurin@dacharry-avocats.fr (E)"),
        At(2, "ENOVACOM      13008 MARSEILLE 08"),
        At(1, "Juridiction 1ère Instance"),
        At(2, "Tribunal de Commerce - Ressort : 33000 BORDEAUX"),
        "Page N° 1");

    [Fact]
    public void ReadsTheDossierNumberAndItsLabel()
    {
        var list = GestisoftContacts.Read(Report());

        Assert.NotNull(list);
        Assert.Equal("700978", list.Code);
        Assert.Equal("TELE MEDECINS DE FRANCE / SILLAND NANCY", list.Label);
    }

    [Fact]
    public void ReadsEveryPartyWithTheRoleGestisoftGaveIt()
    {
        var list = GestisoftContacts.Read(Report())!;

        Assert.Collection(
            list.Contacts,
            contact => Assert.Equal(("TELE MEDECINS DE FRANCE", "Client"), (contact.Name, contact.Role)),
            contact => Assert.Equal(("ORADIANSE - Mme DESRUES Laura", "Interlocuteur client"), (contact.Name, contact.Role)),
            contact => Assert.Equal(("SILLAND Nancy", "Partie adverse"), (contact.Name, contact.Role)),
            contact => Assert.Equal(("SEURIN Héléne", "Avocat de la partie adverse"), (contact.Name, contact.Role)),
            contact => Assert.Equal(("ENOVACOM", "Partie adverse"), (contact.Name, contact.Role)),
            contact => Assert.Equal(("Tribunal de Commerce", "Juridiction"), (contact.Name, contact.Role)));
    }

    /// <summary>
    /// The regression that makes the whole file worth reading twice. An avocat is nested under the
    /// adverse party it acts for, so a parser that simply remembers the last heading gives every later
    /// party the avocat's role. ENOVACOM comes after SEURIN and is a party, not her client's counsel.
    /// </summary>
    [Fact]
    public void EndsAHeadingWhenTheIndentationComesBackOut()
    {
        var list = GestisoftContacts.Read(Report())!;

        Assert.Equal("Partie adverse", Assert.Single(list.Contacts, contact => contact.Name == "ENOVACOM").Role);
    }

    /// <summary>
    /// Taking every bracketed run in the file also collects the information dictionary, and a contacts
    /// list came back with « QuickReports PDF Export » filed as a party to the matter.
    /// </summary>
    [Fact]
    public void LeavesTheDocumentsOwnMetadataOutOfIt()
    {
        var list = GestisoftContacts.Read(Report())!;

        Assert.DoesNotContain(list.Contacts, contact => contact.Name.Contains("QuickReports"));
        Assert.DoesNotContain(list.Contacts, contact => contact.Name.Contains("D:2026"));
    }

    [Fact]
    public void MarksTheOneContactThatIsTheClient()
    {
        var list = GestisoftContacts.Read(Report())!;

        Assert.Equal("TELE MEDECINS DE FRANCE", Assert.Single(list.Contacts, contact => contact.IsClient).Name);
    }

    /// <summary>
    /// « - Ressort » is the column Gestisoft prints a jurisdiction's reach in, left empty here, and it
    /// arrives welded to the name.
    /// </summary>
    [Fact]
    public void DropsTheEmptyRessortColumnFromAJuridiction()
    {
        var list = GestisoftContacts.Read(Report())!;

        Assert.Single(list.Contacts, contact => contact.Name == "Tribunal de Commerce");
    }

    [Fact]
    public void TakesTheTownAndThePostcodeApart()
    {
        var list = GestisoftContacts.Read(Report())!;
        var contact = Assert.Single(list.Contacts, contact => contact.Name == "ENOVACOM");

        Assert.Equal("13008", contact.PostCode);
        Assert.Equal("MARSEILLE 08", contact.City);
    }

    /// <summary>Gestisoft runs the telephone straight on from the town, on the same line.</summary>
    [Fact]
    public void TakesATelephoneBackOffTheEndOfATown()
    {
        var list = GestisoftContacts.Read(
            Pdf("Liste des contacts",
                At(1, "Client"),
                At(2, "ALLIANCE DIFFUSION      33150 CENON 05 56 06 23 39")))!;

        Assert.Equal("CENON", Assert.Single(list.Contacts).City);
    }

    /// <summary>
    /// A surname in capitals followed by a given name that is not. Wrong occasionally, and a fiche with
    /// the wrong type is one click to fix where a missing one is an evening of retyping.
    /// </summary>
    [Fact]
    public void TellsAPersonneMoraleFromAPersonnePhysique()
    {
        var list = GestisoftContacts.Read(Report())!;

        Assert.True(Assert.Single(list.Contacts, contact => contact.Name == "TELE MEDECINS DE FRANCE").IsOrganisation);
        Assert.False(Assert.Single(list.Contacts, contact => contact.Name == "SILLAND Nancy").IsOrganisation);
    }

    [Fact]
    public void AttachesATelephoneAndAnEmailToTheContactAbove()
    {
        var list = GestisoftContacts.Read(Report())!;

        Assert.Equal("05.56.01.81.70", Assert.Single(list.Contacts, contact => contact.Name == "TELE MEDECINS DE FRANCE").Phone);
        Assert.Equal("seurin@dacharry-avocats.fr", Assert.Single(list.Contacts, contact => contact.Name == "SEURIN Héléne").Email);
    }

    /// <summary>
    /// The scan finds the file by its name, and a dossier can hold a letter called « pas de contact
    /// connu chez EDF ». Reading one is how we find out it is not a contacts list.
    /// </summary>
    [Fact]
    public void RefusesAPdfThatIsNotAContactsList()
    {
        Assert.Null(GestisoftContacts.Read(Pdf("Courrier", "Madame, Monsieur,", "Nous n'avons pas de contact connu.")));
    }

    [Fact]
    public void RefusesAFileThatIsNotThere() =>
        Assert.Null(GestisoftContacts.Read(Path.Combine(_folder, "absent.pdf")));

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
