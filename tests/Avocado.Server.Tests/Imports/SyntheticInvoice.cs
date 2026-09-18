using Avocado.Server.Features.Documents.Folders;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Avocado.Server.Tests.Imports;

/// <summary>
/// An invoice PDF laid out the way the cabinet's own tool lays one out.
///
/// <para>Shared because two test classes need a readable invoice: the reader's, and the one that
/// matches a dropped file to a facture already recorded. Real invoices cannot be committed, so this
/// reproduces the layout instead, including the traps: the € of the totals block that comes back as
/// a null character, and the cabinet's own SIREN sitting on the page beside the client's.</para>
/// </summary>
internal static class SyntheticInvoice
{
    private const double Width = 596;

    public static byte[] Build(
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

}
