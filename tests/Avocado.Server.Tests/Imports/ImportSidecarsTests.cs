using System.Text;
using Avocado.Server.Features.Imports;
using Avocado.Server.Features.Imports.Infrastructure;

namespace Avocado.Server.Tests.Imports;

/// <summary>
/// The two files she fills in by hand, which means every shape Excel can produce has to be read
/// rather than refused. A template that rejects what the spreadsheet wrote is a template nobody
/// finishes.
/// </summary>
public class ImportSidecarsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"avocado-sidecar-{Guid.NewGuid():N}");

    public ImportSidecarsTests() => Directory.CreateDirectory(_folder);

    private void Write(string name, params string[] lines) =>
        File.WriteAllLines(Path.Combine(_folder, name), lines, new UTF8Encoding(true));

    [Fact]
    public void ReadsPartiesAgainstTheirDossier()
    {
        Write(ImportSidecars.TiersFileName,
            "dossier;nom;type;role;email;telephone;adresse",
            "ANODEA;SARL Dupont;PM;Client;contact@dupont.fr;05 56 00 00 00;12 rue de Bordeaux");

        var row = Assert.Single(ImportSidecars.Read(_folder).Tiers);

        Assert.Equal("ANODEA", row.Dossier);
        Assert.Equal("SARL Dupont", row.Nom);
        Assert.Equal("Client", row.Role);
        Assert.Equal("contact@dupont.fr", row.Email);
    }

    /// <summary>The template ships one row per dossier with the rest blank. Those are not errors.</summary>
    [Fact]
    public void IgnoresRowsSheHasNotFilledIn()
    {
        Write(ImportSidecars.TiersFileName,
            "dossier;nom;type;role;email;telephone;adresse",
            "ANODEA;;;;;;",
            "VATEL;;;;;;");

        var sidecars = ImportSidecars.Read(_folder);

        Assert.Empty(sidecars.Tiers);
        Assert.Empty(sidecars.Problems);
    }

    /// <summary>
    /// Every way a French spreadsheet writes money, including the non-breaking space Excel uses for
    /// thousands, which is not the space anyone types and is invisible when it goes wrong.
    /// </summary>
    [Theory]
    [InlineData("1234,56", 123_456)]
    [InlineData("1234.56", 123_456)]
    [InlineData("1 234,56", 123_456)]
    [InlineData("1 234,56", 123_456)]
    [InlineData("1.234,56", 123_456)]
    [InlineData("1 234,56 €", 123_456)]
    [InlineData("2400", 240_000)]
    [InlineData("-150,00", -15_000)]
    public void ReadsAmountsInEveryShapeExcelWrites(string written, long expected)
    {
        Write(ImportSidecars.FacturationFileName,
            "dossier;date;type;montant_ht;libelle;reference;paye",
            $"ANODEA;15/08/2026;facture;{written};Honoraires;F-2026-014;oui");

        Assert.Equal(expected, Assert.Single(ImportSidecars.Read(_folder).Facturation).AmountCents);
    }

    [Theory]
    [InlineData("15/08/2026")]
    [InlineData("15/8/2026")]
    [InlineData("2026-08-15")]
    [InlineData("15-08-2026")]
    [InlineData("15.08.2026")]
    public void ReadsDatesInEveryShapeExcelWrites(string written)
    {
        Write(ImportSidecars.FacturationFileName,
            "dossier;date;type;montant_ht;libelle;reference;paye",
            $"ANODEA;{written};facture;1200;Honoraires;;non");

        var row = Assert.Single(ImportSidecars.Read(_folder).Facturation);

        Assert.Equal(2026, row.Date.Year);
        Assert.Equal(8, row.Date.Month);
        Assert.Equal(15, row.Date.Day);
    }

    /// <summary>
    /// One bad line must cost one line. Eighty dossiers failing to import because a comma landed in an
    /// amount on row 41 is not an acceptable answer to a typing mistake.
    /// </summary>
    [Fact]
    public void NamesABadLineAndKeepsTheRest()
    {
        Write(ImportSidecars.FacturationFileName,
            "dossier;date;type;montant_ht;libelle;reference;paye",
            "ANODEA;15/08/2026;facture;1200;Bon;;oui",
            "VATEL;pas une date;facture;900;Mauvais;;non",
            "PIRBAY;16/08/2026;facture;300;Bon aussi;;oui");

        var sidecars = ImportSidecars.Read(_folder);

        Assert.Equal(2, sidecars.Facturation.Count);
        Assert.Contains(sidecars.Problems, problem => problem.Contains("ligne 3", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsAQuotedFieldContainingASemicolon()
    {
        Write(ImportSidecars.FacturationFileName,
            "dossier;date;type;montant_ht;libelle;reference;paye",
            "ANODEA;15/08/2026;facture;1200;\"Honoraires ; frais\";;oui");

        Assert.Equal("Honoraires ; frais", Assert.Single(ImportSidecars.Read(_folder).Facturation).Libelle);
    }

    [Theory]
    [InlineData("oui", true)]
    [InlineData("OUI", true)]
    [InlineData("x", true)]
    [InlineData("1", true)]
    [InlineData("non", false)]
    [InlineData("", false)]
    public void ReadsWhetherItWasPaid(string written, bool expected)
    {
        Write(ImportSidecars.FacturationFileName,
            "dossier;date;type;montant_ht;libelle;reference;paye",
            $"ANODEA;15/08/2026;facture;1200;Honoraires;;{written}");

        Assert.Equal(expected, Assert.Single(ImportSidecars.Read(_folder).Facturation).Paye);
    }

    [Fact]
    public void AnAbsentFileIsNotAProblem()
    {
        var sidecars = ImportSidecars.Read(_folder);

        Assert.Empty(sidecars.Tiers);
        Assert.Empty(sidecars.Facturation);
        Assert.Empty(sidecars.Problems);
    }

    /// <summary>The templates carry the dossier names so the only thing left to type is what we lack.</summary>
    [Fact]
    public void WritesTemplatesPrefilledWithTheDossiers()
    {
        var candidates = new[]
        {
            new ImportCandidate("/x/ANODEA", "ANODEA", "ANODEA", true, 10, 3, 100, 0),
            new ImportCandidate("/x/VATEL", "VATEL", "VATEL", true, 5, 0, 50, 0),
        };

        var written = ImportSidecars.WriteTemplates(_folder, candidates);

        Assert.Equal(2, written.Count);

        var tiers = File.ReadAllLines(Path.Combine(_folder, ImportSidecars.TiersFileName));
        Assert.Equal(3, tiers.Length);
        Assert.StartsWith("dossier;nom;", tiers[0], StringComparison.Ordinal);
        Assert.StartsWith("ANODEA;", tiers[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Running the scan twice must not erase what she typed between the two, which is the whole point
    /// of the exercise.
    /// </summary>
    [Fact]
    public void NeverOverwritesATemplateSheHasStartedFillingIn()
    {
        Write(ImportSidecars.TiersFileName,
            "dossier;nom;type;role;email;telephone;adresse",
            "ANODEA;SARL Dupont;PM;Client;;;");

        var written = ImportSidecars.WriteTemplates(
            _folder,
            [new ImportCandidate("/x/ANODEA", "ANODEA", "ANODEA", true, 1, 0, 1, 0)]);

        Assert.DoesNotContain(written, path => path.EndsWith(ImportSidecars.TiersFileName, StringComparison.Ordinal));
        Assert.Equal("SARL Dupont", Assert.Single(ImportSidecars.Read(_folder).Tiers).Nom);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
