using Avocado.Server.Features.Documents.Folders;
using Avocado.Server.Features.Imports.Infrastructure;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Avocado.Server.Tests.Imports;

/// <summary>
/// Reading a facture she issued, out of the PDF her invoicing tool produced.
///
/// <para><b>The fixtures are synthetic, and that is deliberate.</b> This was built against two real
/// invoices from the cabinet, and those cannot live here: this repository is public and they carry
/// the cabinet's IBAN, its SIREN, the clients' names, their addresses and what they were charged.
/// So the layout is reproduced instead, trap for trap, and the real ones were checked by hand.</para>
/// </summary>
public class InvoiceReaderTests
{
    private const double Width = 596;

    /// <summary>
    /// An invoice laid out like theirs: number and dates top right, the cabinet left and the client
    /// right, then the totals block.
    /// </summary>
    /// <param name="currency">
    /// What follows the figures in the totals block. « \0 » is not a curiosity: on the real invoices
    /// the € of that block comes back from the font as a null character, because its encoding does
    /// not map the glyph. That NUL is not whitespace, so it survives trimming, and it made every
    /// total on every invoice read as absent.
    /// </param>
    private static byte[] Invoice(
        string number = "92",
        string issued = "14/09/2026",
        string due = "14/10/2026",
        string client = "N'JOY MOMENTS",
        string clientSiren = "828 768 028",
        string subject = "Litige PHOENIX",
        string ht = "600,00",
        string tva = "120,00",
        string ttc = "720,00",
        bool paid = true,
        string currency = "\0")
    {
        StampFont.Install();

        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(Width);
        page.Height = XUnit.FromPoint(842);

        using var canvas = XGraphics.FromPdfPage(page);
        var font = new XFont(StampFont.FaceName, 9);

        void At(double x, double y, string text) =>
            canvas.DrawString(text, font, XBrushes.Black, new XPoint(x, y));

        // On its own line, as on the real invoices. Drawn overlapping the line below, PdfPig
        // interleaves the two strings letter by letter: « Payée » over « Facture » came back as
        // « PayéecaFture ». Worth knowing about the extractor, and worth not writing into a fixture.
        if (paid)
        {
            At(466, 20, "Payée");
        }

        At(480, 36, $"Facture N° {number}");
        At(430, 54, $"Date d'émission : {issued}");
        At(430, 70, $"Date limite de paiement : {due}");

        // The cabinet, left. Its own SIREN is on the page too, and must never be taken for the
        // client's: that is the whole reason the reader splits the page into columns.
        At(18, 120, "PASCAUD-BLANDIN PERLE (EI)");
        At(18, 134, "49 Rue CAMILLE GODARD 33000 BORDEAUX, FR");
        At(18, 148, "p.pascaud-blandin@majele.avocats.fr");
        At(18, 162, "SIREN : 921 267 191");

        // The client, right.
        At(492, 120, client);
        At(480, 134, "31 Avenue DE SEGUR 75007 PARIS, FR");
        At(492, 148, $"SIREN : {clientSiren}");

        At(18, 200, subject);
        At(18, 240, "Détails Qté Prix unitaire TVA % Total HT");
        At(18, 260, $"Provision pour rédaction d'un courrier 1 {ht} € 20 % {ht} €");

        At(374, 300, $"Total HT {ht} {currency}");
        At(374, 316, $"TVA {tva} €");
        At(374, 332, $"Total {ttc} {currency}");

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }

    private static ReadInvoice Read(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return InvoiceReader.Read(stream);
    }

    [Fact]
    public void ReadsWhatAvocadoNeedsToRecordAFacture()
    {
        var read = Read(Invoice());

        Assert.Equal("92", read.Number);
        Assert.Equal(new DateOnly(2026, 9, 14), read.IssuedOn);
        Assert.Equal(new DateOnly(2026, 10, 14), read.DueOn);
        Assert.Equal(60_000, read.TotalExclVatCents);
        Assert.Equal(12_000, read.VatCents);
        Assert.Equal(72_000, read.TotalInclVatCents);
        Assert.True(read.IsUsable);
    }

    /// <summary>
    /// The one that cost an afternoon on the real invoices, and would have made the feature useless
    /// while looking like it worked: every total read as absent.
    /// </summary>
    [Fact]
    public void ReadsATotalWhoseCurrencySymbolCameBackAsANullCharacter()
    {
        var read = Read(Invoice(currency: "\0"));

        Assert.Equal(60_000, read.TotalExclVatCents);
        Assert.Equal(72_000, read.TotalInclVatCents);
    }

    [Fact]
    public void ReadsATotalWhoseCurrencySymbolCameBackProperly()
    {
        var read = Read(Invoice(currency: "€"));

        Assert.Equal(60_000, read.TotalExclVatCents);
        Assert.Equal(72_000, read.TotalInclVatCents);
    }

    /// <summary>
    /// An invoice states HT, TVA and TTC, so the reading can be checked against its own arithmetic.
    /// A total lifted from the wrong line almost never balances.
    /// </summary>
    [Fact]
    public void ChecksTheInvoiceAgainstItsOwnArithmetic()
    {
        Assert.True(Read(Invoice()).Balances);

        var wrong = Read(Invoice(ht: "600,00", tva: "120,00", ttc: "999,00"));

        Assert.False(wrong.Balances);
        Assert.Contains(wrong.Notes, note => note.Contains("s'accordent pas"));
    }

    /// <summary>
    /// Two SIRENs on the page, the cabinet's and the client's. Taking the wrong one would attach
    /// every facture to a dossier for the cabinet itself.
    /// </summary>
    [Fact]
    public void TakesTheClientsSirenAndNotTheCabinetsOwn()
    {
        var read = Read(Invoice(clientSiren: "828 768 028"));

        Assert.Equal("828768028", read.ClientSiren);
        Assert.NotEqual("921267191", read.ClientSiren);
        Assert.Equal("N'JOY MOMENTS", read.ClientName);
    }

    [Fact]
    public void ReadsWhetherItHasBeenPaid()
    {
        Assert.True(Read(Invoice(paid: true)).Paid);
        Assert.False(Read(Invoice(paid: false)).Paid);
    }

    [Fact]
    public void ReadsWhatTheFactureIsFor()
    {
        var read = Read(Invoice(subject: "Honoraires pour assistance juridique dans le cadre du litige LVMH"));

        Assert.Equal("Honoraires pour assistance juridique dans le cadre du litige LVMH", read.Subject);
    }

    [Fact]
    public void ReadsAFourFigureTotal()
    {
        var read = Read(Invoice(ht: "1080,00", tva: "216,00", ttc: "1296,00"));

        Assert.Equal(108_000, read.TotalExclVatCents);
        Assert.True(read.Balances);
    }

    [Fact]
    public void SaysSoRatherThanGuessingWhenThereIsNoSiren()
    {
        var read = Read(Invoice(clientSiren: ""));

        Assert.Null(read.ClientSiren);
        Assert.Contains(read.Notes, note => note.Contains("SIREN"));
        // Still usable: the amount and the date are what a facture needs, and she picks the dossier.
        Assert.True(read.IsUsable);
    }

    [Theory]
    [InlineData("600,00", 60_000L)]
    [InlineData("1 080,00", 108_000L)]
    [InlineData("1080.00", 108_000L)]
    [InlineData("240", 24_000L)]
    [InlineData("pas un nombre", null)]
    public void ReadsFrenchAmounts(string written, long? expected) =>
        Assert.Equal(expected, InvoiceReader.Cents(written));

    [Theory]
    // The ﬁ ligature is one character: « Modiﬁcation » does not contain « fi ».
    [InlineData("Modiﬁcation", "Modification")]
    [InlineData("Date d’émission", "Date d'émission")]
    [InlineData("1 080", "1 080")]
    [InlineData("Total  HT   600", "Total HT 600")]
    [InlineData("600,00 \0", "600,00")]
    public void NormalisesWhatAPdfActuallyYields(string raw, string expected) =>
        Assert.Equal(expected, InvoiceReader.Normalise(raw));
}
