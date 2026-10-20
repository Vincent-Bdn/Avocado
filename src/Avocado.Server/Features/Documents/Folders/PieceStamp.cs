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
/// <param name="OpaqueBackground">
/// A white rectangle behind the stamp, so the pièce shows through it nowhere.
///
/// <para>Most cabinet stamps are a transparent PNG or an SVG drawn on nothing, which is the right
/// way to store a logo and the wrong way to land it on a scanned pièce: the text of the document
/// comes through the gaps, crosses the dotted line, and the pièce number becomes hard to read on
/// exactly the documents where it matters. « Mettre sur fond blanc et pas transparent. »</para>
///
/// <para><b>Off by default, deliberately.</b> It covers whatever it is drawn over, and the top-right
/// corner of a conclusion or a facture is not always empty. Every practice already stamping has
/// lined its stamp up against what the page looks like now, so this is hers to turn on.</para>
/// </param>
public sealed record StampOptions(
    StampCorner Corner = StampCorner.TopRight,
    // About 3 cm. It was 120, which is 4,2 cm and reads as large on an A4: « réduire la taille du
    // tampon ». A practice that has already saved a width keeps it; this is what a new one starts at.
    double WidthPoints = 85,
    double MarginPoints = 18,
    double NumberXPercent = 62,
    double NumberYPercent = 92,
    double NumberSizePoints = 11,
    string? NumberColour = null,
    bool EveryPage = false,
    bool OpaqueBackground = false);

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
    /// Photographs, which become a PDF page before they are stamped.
    ///
    /// <para>Half of what a cabinet verses is a photograph: a roof, a wall, a signed devis snapped on
    /// a telephone. She used to convert each one by hand before versing it, because a stamp only goes
    /// on a PDF, and nothing said so.</para>
    ///
    /// <para>Raster only. A .docx would mean driving Word, which means Word installed and a second
    /// thing to go wrong; those are still copied as they are and named as not stamped.</para>
    /// </summary>
    public static readonly string[] PictureExtensions = [".jpg", ".jpeg", ".png"];

    public static bool IsPicture(string fileName) =>
        PictureExtensions.Any(extension =>
            fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether this file can be stamped at all. Extension, not content: she picks these from her own
    /// folder and « il faut un PDF » is a sentence she can act on.
    /// </summary>
    public static bool CanStamp(string fileName) =>
        fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || IsPicture(fileName);

    /// <summary>
    /// A photograph as a single A4 page, which is what the rest of the bundle is.
    ///
    /// <para>Fitted inside the margins rather than bled to the edges: a pièce is read next to the
    /// others, printed, and sometimes hole-punched, and a photograph running under the stamp in the
    /// corner is the one thing this must not produce.</para>
    /// </summary>
    public static byte[] PageFromPicture(byte[] picture)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();

        using var stream = new MemoryStream(picture);
        using var image = XImage.FromStream(stream);
        using var canvas = XGraphics.FromPdfPage(page);

        const double margin = 36;

        var room = new XSize(page.Width.Point - (2 * margin), page.Height.Point - (2 * margin));
        var scale = Math.Min(
            room.Width / Math.Max(1, image.PixelWidth),
            room.Height / Math.Max(1, image.PixelHeight));

        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;

        canvas.DrawImage(
            image,
            (page.Width.Point - width) / 2,
            (page.Height.Point - height) / 2,
            width,
            height);

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);

        return output.ToArray();
    }

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

        RaiseForTransparency(document);

        using var imageStream = new MemoryStream(stampImage);
        using var image = XImage.FromStream(imageStream);

        var stampHeight = options.WidthPoints * image.PixelHeight / Math.Max(1, image.PixelWidth);

        foreach (var page in Pages(document, options.EveryPage))
        {
            using var canvas = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            // The page as a reader sees it, which is not how it is stored. A scan almost always
            // carries /Rotate, and drawing in the stored orientation put the stamp sideways, in a
            // corner that is not the top right, sometimes half off the paper. « Le tampon ne s'est
            // pas mis » is what that looks like from the other side of the screen.
            var (pageWidth, pageHeight) = Upright(canvas, page);

            var x = options.Corner is StampCorner.TopLeft or StampCorner.BottomLeft
                ? options.MarginPoints
                : pageWidth - options.WidthPoints - options.MarginPoints;

            var y = options.Corner is StampCorner.TopLeft or StampCorner.TopRight
                ? options.MarginPoints
                : pageHeight - stampHeight - options.MarginPoints;

            // Under the stamp, so a transparent logo stops letting the pièce through. Drawn rather
            // than composited into the image: her file stays the file she gave us, and turning this
            // off puts the page back exactly as it was.
            if (options.OpaqueBackground)
            {
                canvas.DrawRectangle(XBrushes.White, x, y, options.WidthPoints, stampHeight);
            }

            canvas.DrawImage(image, x, y, options.WidthPoints, stampHeight);

            // Drawn rather than composited into the image, so one stamp file serves every number and
            // she never has to produce sixty variants of her own logo.
            var font = new XFont(StampFont.FaceName, options.NumberSizePoints, XFontStyleEx.Bold);

            canvas.DrawString(
                number.ToString(),
                font,
                new XSolidBrush(Ink(options.NumberColour)),
                new XRect(
                    x + (options.WidthPoints * options.NumberXPercent / 100),
                    y + (stampHeight * options.NumberYPercent / 100) - options.NumberSizePoints,
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
    /// The version a soft mask needs, when the pièce was written before soft masks existed.
    ///
    /// <para><b>The black square.</b> A transparent stamp is carried into a PDF as a soft mask, and
    /// soft masks arrived in PDF 1.4. Asked to write one into a 1.3 document, PDFsharp does the
    /// correct thing and leaves it out, which turns the transparent parts opaque. Her logo is dark
    /// green drawn on nothing, and « nothing » is stored as black with an alpha of zero, so dropping
    /// the alpha painted a black tile over the page and swallowed the number with it.</para>
    ///
    /// <para>Four of her five pièces came out of ReportLab, which still writes 1.3. Nothing was wrong
    /// with her image, and nothing was wrong with the tile: it never existed.</para>
    ///
    /// <para>Raising the version is the honest repair rather than a trick. The file genuinely now
    /// uses a 1.4 feature, so it should genuinely say 1.4; the header is a floor, readers have
    /// handled it since 2001, and a stamped pièce is a new document anyway. Only ever raised, so a
    /// modern pièce keeps whatever it already declared.</para>
    /// </summary>
    private static void RaiseForTransparency(PdfDocument document)
    {
        const int softMasksArrived = 14;

        if (document.Version < softMasksArrived)
        {
            document.Version = softMasksArrived;
        }
    }

    /// <summary>
    /// Turns the canvas so that drawing happens in the orientation a reader sees, and gives back the
    /// page's size in that orientation.
    ///
    /// <para><b>/Rotate is not a curiosity.</b> A page carries its content one way and an instruction
    /// to display it another, and scanners set it constantly: a pièce photographed sideways is stored
    /// sideways with /Rotate 270 rather than being re-encoded. PDFsharp draws in the stored space, so
    /// « en haut à droite » landed wherever that corner happens to be once the viewer has turned the
    /// page, which for a quarter turn is a different corner and a stamp lying on its side.</para>
    ///
    /// <para>Returns the visible width and height, swapped for a quarter turn, so the corner
    /// arithmetic above needs to know nothing about any of this.</para>
    /// </summary>
    private static (double Width, double Height) Upright(XGraphics canvas, PdfPage page)
    {
        var width = page.Width.Point;
        var height = page.Height.Point;

        // Negative and over-360 values both occur in the wild; /Rotate is only meaningful in
        // quarter turns, and anything else is rounded to the nearest one rather than refused.
        var turns = ((int)Math.Round(page.Elements.GetInteger("/Rotate") / 90.0) % 4 + 4) % 4;

        switch (turns)
        {
            // A quarter turn clockwise for the reader, so the page's bottom-left corner is what he
            // sees top-left. Drawing at visible (vx, vy) has to land on stored (vy, height − vx).
            case 1:
                canvas.TranslateTransform(0, height);
                canvas.RotateTransform(-90);
                return (height, width);

            case 2:
                canvas.TranslateTransform(width, height);
                canvas.RotateTransform(180);
                return (width, height);

            // The other way: the page's top-right corner is what he sees top-left.
            case 3:
                canvas.TranslateTransform(width, 0);
                canvas.RotateTransform(90);
                return (height, width);

            default:
                return (width, height);
        }
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
