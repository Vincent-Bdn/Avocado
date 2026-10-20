using Avocado.Server.Features.Documents.Folders;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Avocado.Server.Tests.Documents;

/// <summary>
/// Stamping a pièce.
///
/// <para>These run on Windows, Linux and macOS in CI, deliberately. Eight of the ten practices in the
/// beta are on Macs, PDF libraries are where font and image handling differ most between platforms,
/// and the last thing that shipped broken on macOS was broken for exactly that kind of reason.</para>
/// </summary>
public class PieceStampTests
{
    /// <summary>A real 1×1 PNG. Small enough to inline, real enough for an image decoder.</summary>
    private static byte[] Stamp() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static byte[] Pdf(int pages)
    {
        using var document = new PdfDocument();

        for (var index = 0; index < pages; index++)
        {
            document.AddPage();
        }

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }

    private static PdfDocument Reopen(byte[] bytes) =>
        PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    /// <summary>
    /// What the page actually tells a reader to draw.
    ///
    /// <para>Page counts and byte lengths are what these tests checked for months, and both were
    /// perfectly happy while the number was being drawn in black on a black logo. The operators are
    /// the only place the colour exists.</para>
    /// </summary>
    private static string Drawn(byte[] stamped)
    {
        using var document = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Modify);

        return string.Join(
            '\n',
            document.Pages[0].Contents.Elements
                .OfType<PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfDictionary>()
                .Select(stream => stream.Stream.ToString()));
    }

    [Fact]
    public void StampsAPdfWithoutLosingAPage()
    {
        var stamped = PieceStamp.Apply(Pdf(3), Stamp(), 7, new StampOptions());

        using var document = Reopen(stamped);

        Assert.Equal(3, document.PageCount);
        Assert.True(stamped.Length > 0);
    }

    /// <summary>
    /// « Mettre sur fond blanc et pas transparent. »
    ///
    /// <para>A transparent logo lets the pièce through it, so the text of a scan crosses the dotted
    /// line and the numéro becomes hard to read. The fill operator is the only place the rectangle
    /// exists: a byte count would be just as happy without it.</para>
    /// </summary>
    [Fact]
    public void PutsAWhiteRectangleUnderTheStampWhenAskedTo()
    {
        var opaque = Drawn(PieceStamp.Apply(
            Pdf(1), Stamp(), 7, new StampOptions(OpaqueBackground: true)));

        // « 1 1 1 rg » is white in a PDF content stream, followed by a rectangle and a fill.
        Assert.Contains("1 1 1 rg", opaque);
        Assert.Contains(" re", opaque);
    }

    /// <summary>
    /// And nothing at all by default: it covers what it is drawn over, and the top-right corner of a
    /// conclusion is not always empty. Every practice already stamping has lined its stamp up against
    /// what the page looks like now.
    /// </summary>
    [Fact]
    public void DrawsNoBackgroundUnlessAsked()
    {
        Assert.DoesNotContain("1 1 1 rg", Drawn(PieceStamp.Apply(Pdf(1), Stamp(), 7, new StampOptions())));
    }

    /// <summary>
    /// The stamp a new practice gets is about 3 cm wide, not 4,2. « Réduire la taille du tampon. »
    /// A practice that has already saved a width keeps it; this is only the starting point.
    /// </summary>
    [Fact]
    public void StartsAtAboutThreeCentimetres()
    {
        // 85 points at 72 to the inch is 2,99 cm.
        Assert.Equal(85, new StampOptions().WidthPoints);
    }

    [Fact]
    public void LeavesTheOriginalBytesAlone()
    {
        var original = Pdf(1);
        var copy = original.ToArray();

        PieceStamp.Apply(original, Stamp(), 1, new StampOptions());

        // The file in her folder is not ours to modify: stamping produces a new document in Pièces.
        Assert.Equal(copy, original);
    }

    [Fact]
    public void StampsOnlyTheFirstPageByDefault()
    {
        // Their own tool's choice, written down in the mode d'emploi: « Pages tamponnées : première
        // page uniquement ». A stamped page is longer than an untouched one, which is what says so.
        var plain = Pdf(2);
        var stamped = PieceStamp.Apply(plain, Stamp(), 3, new StampOptions());

        Assert.True(stamped.Length > plain.Length);

        var everyPage = PieceStamp.Apply(plain, Stamp(), 3, new StampOptions(EveryPage: true));

        Assert.True(
            everyPage.Length > stamped.Length,
            $"Every page produced {everyPage.Length} bytes, first page alone {stamped.Length}.");
    }

    [Theory]
    [InlineData(StampCorner.TopRight)]
    [InlineData(StampCorner.TopLeft)]
    [InlineData(StampCorner.BottomRight)]
    [InlineData(StampCorner.BottomLeft)]
    public void PutsTheStampInEveryCornerWithoutFalling(StampCorner corner)
    {
        var stamped = PieceStamp.Apply(Pdf(1), Stamp(), 12, new StampOptions(Corner: corner));

        using var document = Reopen(stamped);
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void MergesThePiecesInTheOrderTheyWereGiven()
    {
        var merged = PieceStamp.Merge([Pdf(1), Pdf(2), Pdf(3)]);

        using var document = Reopen(merged);

        Assert.Equal(6, document.PageCount);
    }


    [Theory]
    [InlineData("Assignation.pdf", true)]
    [InlineData("assignation.PDF", true)]
    // Photographs are half of what gets versed, and she was converting each one by hand.
    [InlineData("Toiture.jpg", true)]
    [InlineData("Toiture.JPEG", true)]
    [InlineData("Capture.png", true)]
    // Still out: converting these means driving Word, and Word is a second thing to go wrong.
    [InlineData("Conclusions.docx", false)]
    [InlineData("Courriel du 3 mars.msg", false)]
    [InlineData("Plan.dwg", false)]
    public void SaysWhatItCanStamp(string fileName, bool expected) =>
        Assert.Equal(expected, PieceStamp.CanStamp(fileName));

    /// <summary>
    /// A photograph becomes one A4 page, fitted inside the margins.
    ///
    /// <para>Bled to the edges it would run under the stamp, which is the one thing a numbered pièce
    /// must not do.</para>
    /// </summary>
    [Fact]
    public void TurnsAPhotographIntoAPageTheStampFitsOn()
    {
        var page = PieceStamp.PageFromPicture(Stamp());

        using var document = Reopen(page);

        Assert.Equal(1, document.PageCount);
        Assert.Equal(595, document.Pages[0].Width.Point, 0);
        Assert.Equal(842, document.Pages[0].Height.Point, 0);

        // And it stamps like anything else, which is the whole point of converting it.
        var landed = Landed(PieceStamp.Apply(page, TransparentStamp(), 4, new StampOptions()));

        Assert.Equal(new StampOptions().MarginPoints, landed.Top, 1);
    }

    /// <summary>
    /// A file that is not a PDF, or one that is truncated, throws rather than producing something.
    ///
    /// <para>Which is what the batch wants: it catches per file and reports that one as unstamped,
    /// rather than losing the other fourteen. What must never happen is a file written into Pièces
    /// that opens as garbage, because that one gets filed.</para>
    ///
    /// <para>(A PDF with no pages is not tested, because it cannot be made: PDFsharp refuses to save
    /// one, so the case does not exist outside a deliberately corrupt file, which is this test.)</para>
    /// </summary>
    [Fact]
    public void RefusesRubbishRatherThanWritingIt()
    {
        var notAPdf = System.Text.Encoding.UTF8.GetBytes("Ceci n'est pas un PDF");

        Assert.ThrowsAny<Exception>(() => PieceStamp.Apply(notAPdf, Stamp(), 1, new StampOptions()));
    }

    /// <summary>
    /// The fill colour in force where the digit is drawn.
    ///
    /// <para>Not « does the stream mention 1 1 1 rg » : PDFsharp opens every page by setting black,
    /// so the stream mentions black whatever the number is. What decides what a reader sees is the
    /// last colour set before the glyph.</para>
    /// </summary>
    private static string InkAt(byte[] stamped, string glyph)
    {
        var drawn = Drawn(stamped);
        var at = drawn.IndexOf(glyph, StringComparison.Ordinal);

        Assert.True(at >= 0, $"« {glyph} » is not drawn at all.\n{drawn}");

        var operators = drawn[..at]
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.EndsWith(" rg", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(operators);

        return operators[^1];
    }

    /// <summary>
    /// The one that reached a greffe. A cabinet whose stamp is a black square got a digit at the
    /// right coordinates, in black, on every pièce it filed, and nobody could see it.
    /// </summary>
    [Fact]
    public void WritesTheNumberInTheColourItWasGiven() =>
        Assert.Equal(
            "1 1 1 rg",
            InkAt(PieceStamp.Apply(Pdf(1), Stamp(), 7, new StampOptions(NumberColour: "#FFFFFF")), "(7) Tj"));

    [Fact]
    public void StaysBlackWhenNobodyHasChosen() =>
        Assert.Equal("0 0 0 rg", InkAt(PieceStamp.Apply(Pdf(1), Stamp(), 7, new StampOptions()), "(7) Tj"));

    /// <summary>
    /// Hand-edited nonsense in the settings row costs a badly-coloured number, never the batch.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("blanc")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("#FFFFFFFF")]
    public void FallsBackToBlackRatherThanThrowing(string colour) =>
        Assert.Equal(
            "0 0 0 rg",
            InkAt(PieceStamp.Apply(Pdf(1), Stamp(), 7, new StampOptions(NumberColour: colour)), "(7) Tj"));

    [Theory]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("ff8800", 255, 136, 0)]
    [InlineData("#1A2B3C", 26, 43, 60)]
    public void ReadsAColourWithOrWithoutItsHash(string colour, int red, int green, int blue)
    {
        var ink = PieceStamp.Ink(colour);

        Assert.Equal(red, ink.R);
        Assert.Equal(green, ink.G);
        Assert.Equal(blue, ink.B);
    }

    /// <summary>
    /// A 2×2 PNG carrying real transparency, with black under the transparent pixels: the shape her
    /// own stamp has, because that is what a design tool writes for « fond transparent ».
    /// </summary>
    private static byte[] TransparentStamp() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAD0lEQVR4nGPgNFT7z4AMABPnAWAZtobsAAAAAElFTkSuQmCC");

    /// <summary>A pièce written before soft masks existed. ReportLab still produces these.</summary>
    private static byte[] OldPdf()
    {
        using var document = new PdfDocument();
        document.AddPage();
        document.Version = 13;

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }

    /// <summary>Whether the stamp's transparency survived into the file, which is the whole point.</summary>
    private static bool KeptItsTransparency(byte[] stamped)
    {
        using var document = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Modify);

        return document.Internals.GetAllObjects()
            .OfType<PdfDictionary>()
            .Any(item =>
                item.Elements.GetName("/Subtype") == "/Image"
                && item.Elements.ContainsKey("/SMask"));
    }

    /// <summary>
    /// The black square that reached a greffe on four pièces out of five.
    ///
    /// <para>Her stamp is dark green artwork on nothing. « Nothing » is stored as black at zero
    /// alpha, so the moment the soft mask is dropped the tile turns black and takes the number with
    /// it. Everyone looked at the image and the image was never the problem.</para>
    /// </summary>
    [Fact]
    public void KeepsAStampTransparentOverAPieceOlderThanSoftMasks()
    {
        var stamped = PieceStamp.Apply(OldPdf(), TransparentStamp(), 7, new StampOptions());

        Assert.True(
            KeptItsTransparency(stamped),
            "The soft mask was dropped, so the stamp is an opaque rectangle over her pièce.");
    }

    /// <summary>Raised, never lowered: a modern pièce keeps the version it already declared.</summary>
    [Fact]
    public void LeavesANewerPieceAtItsOwnVersion()
    {
        var stamped = PieceStamp.Apply(Pdf(1), TransparentStamp(), 7, new StampOptions());

        using var document = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Modify);

        Assert.True(document.Version >= 17, $"Version fell to 1.{document.Version}.");
    }

    private static byte[] Rotated(int degrees)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Rotate = degrees;

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }

    /// <summary>
    /// Where the stamp ends up <b>as a reader sees it</b>.
    ///
    /// <para>Read with PdfPig rather than the library that wrote the file, which is the point: it
    /// resolves /Rotate itself and reports the page landscape when the page is displayed landscape,
    /// so what comes back is what a greffier would see and not what PDFsharp intended.</para>
    /// </summary>
    private static (double Top, double Right, double Width, double PageWidth) Landed(
        byte[] stamped,
        double stampWidth = 120)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(stamped);
        var page = document.GetPage(1);

        // A converted photograph puts its own image on the page too, so the stamp is picked by the
        // width it was asked to be rather than by being the only thing there.
        var bounds = page.GetImages()
            .OrderBy(image => Math.Abs(image.Bounds.Width - stampWidth))
            .First()
            .Bounds;

        // PdfPig measures y up from the bottom; the margin is quoted from the top.
        return (page.Height - bounds.Top, bounds.Right, bounds.Width, page.Width);
    }

    /// <summary>
    /// A scanner photographs a pièce sideways and writes /Rotate rather than re-encoding it, so this
    /// is the ordinary case for anything that came off a scanner rather than out of Word.
    ///
    /// <para>Drawing in the stored space put the stamp on its side, in a corner that is not the top
    /// right, and for a quarter turn partly off the paper. From her side that reads as « le tampon
    /// ne s'est pas mis ».</para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void PutsTheStampWhereTheReaderLooksWhateverTheScannerWrote(int degrees)
    {
        var options = new StampOptions();
        var landed = Landed(PieceStamp.Apply(Rotated(degrees), Stamp(), 7, options));

        // Upright: a stamp lying on its side is as wide as it is tall only by accident, so check the
        // width against the width that was asked for.
        Assert.Equal(options.WidthPoints, landed.Width, 1);

        // And in the corner a greffe expects, measured on the page as displayed.
        Assert.Equal(options.MarginPoints, landed.Top, 1);
        Assert.Equal(landed.PageWidth - options.MarginPoints, landed.Right, 1);
    }
}
