using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Avocado.Server.Features.Documents.Folders;

/// <summary>Where on the page the stamp goes. Her own tool offered the four corners.</summary>
public enum StampCorner
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
}

/// <param name="Corner">Top right by default, which is where a greffe expects to find it.</param>
/// <param name="WidthPoints">
/// The stamp's width on the page, in points. Her tool said « 250 px »; a point is 1/72 inch and an
/// A4 page is 595 wide, so 250 would be nearly half the width. 120 is about four centimetres.
/// </param>
/// <param name="MarginPoints">From the edges of the page.</param>
/// <param name="NumberXPercent">
/// Where inside the stamp the number is written, as a percentage of the stamp's own width and height.
/// The supplied image already draws « Pièce n° ....... », so the number lands on the dotted line
/// rather than anywhere Avocado decides.
/// </param>
/// <param name="NumberSizePoints">Font size of the number.</param>
/// <param name="NumberColour">
/// « #RRGGBB », or null while nobody has chosen.
///
/// <para>This used to be black, always, and it was not a default so much as an oversight. A cabinet
/// whose stamp is a black square got a black number drawn on it: the digit was in the page, at the
/// right coordinates, and invisible. The bordereau said pièce 1 and the pièce showed « Pièce n°
/// ....... » with the dots still empty.</para>
///
/// <para>Null rather than black so that the two cases can be told apart. Black here is a colour she
/// picked; null is nobody having been asked. Réglages resolves null from the image itself the first
/// time it shows the panel, and writes the answer back, so what the preview draws and what the
/// pièces carry are never two different things.</para>
/// </param>
/// <param name="EveryPage">
/// False by default, and the default is the one her own tool chose: première page uniquement.
/// Configurable because « page 3/7 » on every page is how some cabinets prove nothing was removed.
/// </param>
public sealed record StampOptions(
    StampCorner Corner = StampCorner.TopRight,
    double WidthPoints = 120,
    double MarginPoints = 18,
    double NumberXPercent = 62,
    double NumberYPercent = 92,
    double NumberSizePoints = 11,
    string? NumberColour = null,
    bool EveryPage = false);

/// <summary>
/// Puts the cabinet's stamp and a pièce number onto a PDF.
///
/// <para><b>Why this is in Avocado at all.</b> The cabinet had already built the tool twice, once as a
/// web page and once as a Python executable with its own installer, because numbering and stamping a
/// bundle of pièces before filing them is a job that comes round every few weeks and that nothing
/// else does. It belongs next to the dossier whose pièces they are.</para>
///
/// <para><b>Only PDFs.</b> A .docx or a .msg cannot be stamped without first being converted, which
/// means driving Word, which means Word being installed and a second thing to go wrong. The tab says
/// so rather than converting silently and producing something that does not look like the original.</para>
///
/// <para>The original is never touched: stamping reads a file and writes a new one into
/// <c>Pièces/</c>, under the name <c>Pièce 7 - Libellé.pdf</c>.</para>
/// </summary>
public static class PieceStamp
{
    /// <summary>
    /// Whether this file can be stamped at all. Extension, not content: she picks these from her own
    /// folder and « it must be a PDF » is a sentence she can act on.
    /// </summary>
    public static bool CanStamp(string fileName) =>
        fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Stamps <paramref name="pdf"/> and returns the new document's bytes.
    ///
    /// <para>The stamp is drawn in the page's own coordinate space, after whatever is already there,
    /// so it sits on top of the content rather than behind it. A page that is rotated or is not A4 is
    /// handled by asking the page for its own width and height rather than assuming.</para>
    /// </summary>
    public static byte[] Apply(byte[] pdf, byte[] stampImage, int number, StampOptions options)
    {
        StampFont.Install();

        using var input = new MemoryStream(pdf);

        // Modify, not Import: the document keeps its own pages, bookmarks and metadata, and only
        // gains a drawing. Importing would rebuild it and lose everything that is not a page.
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        using var imageStream = new MemoryStream(stampImage);
        using var image = XImage.FromStream(imageStream);

        var height = options.WidthPoints * image.PixelHeight / Math.Max(1, image.PixelWidth);

        foreach (var page in Pages(document, options.EveryPage))
        {
            using var canvas = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            var x = options.Corner is StampCorner.TopLeft or StampCorner.BottomLeft
                ? options.MarginPoints
                : page.Width.Point - options.WidthPoints - options.MarginPoints;

            var y = options.Corner is StampCorner.TopLeft or StampCorner.TopRight
                ? options.MarginPoints
                : page.Height.Point - height - options.MarginPoints;

            canvas.DrawImage(image, x, y, options.WidthPoints, height);

            // Drawn rather than composited into the image, so one stamp file serves every number and
            // she never has to produce sixty variants of her own logo.
            var font = new XFont(StampFont.FaceName, options.NumberSizePoints, XFontStyleEx.Bold);

            canvas.DrawString(
                number.ToString(),
                font,
                new XSolidBrush(Ink(options.NumberColour)),
                new XRect(
                    x + (options.WidthPoints * options.NumberXPercent / 100),
                    y + (height * options.NumberYPercent / 100) - options.NumberSizePoints,
                    options.WidthPoints,
                    options.NumberSizePoints * 1.4),
                XStringFormats.TopLeft);
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);

        return output.ToArray();
    }

    /// <summary>
    /// One PDF holding every pièce in order, for the copy that is filed in one go.
    ///
    /// <para>Offered beside the separate files rather than instead of them, because the two are used
    /// for different things: the RPVA wants one document, the client wants the pièce he asked about.</para>
    /// </summary>
    public static byte[] Merge(IEnumerable<byte[]> documents)
    {
        using var merged = new PdfDocument();

        foreach (var bytes in documents)
        {
            using var source = new MemoryStream(bytes);
            using var one = PdfReader.Open(source, PdfDocumentOpenMode.Import);

            for (var index = 0; index < one.PageCount; index++)
            {
                merged.AddPage(one.Pages[index]);
            }
        }

        using var output = new MemoryStream();
        merged.Save(output, closeStream: false);

        return output.ToArray();
    }

    /// <summary>
    /// « #RRGGBB » as a colour, black for anything else.
    ///
    /// <para>Black is the fallback rather than an exception because this runs in the middle of
    /// versing a batch: a colour that was hand-edited into the settings row and is now nonsense
    /// should cost a badly-coloured number, not fifteen pièces.</para>
    /// </summary>
    internal static XColor Ink(string? colour)
    {
        var text = colour?.TrimStart('#');

        return text?.Length == 6
            && int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? XColor.FromArgb((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF)
            : XColors.Black;
    }

    private static IEnumerable<PdfPage> Pages(PdfDocument document, bool everyPage)
    {
        if (document.PageCount == 0)
        {
            yield break;
        }

        if (!everyPage)
        {
            yield return document.Pages[0];
            yield break;
        }

        for (var index = 0; index < document.PageCount; index++)
        {
            yield return document.Pages[index];
        }
    }
}
