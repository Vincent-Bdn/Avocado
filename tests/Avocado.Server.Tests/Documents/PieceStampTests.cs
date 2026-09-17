using Avocado.Server.Features.Documents.Folders;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
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

    [Fact]
    public void StampsAPdfWithoutLosingAPage()
    {
        var stamped = PieceStamp.Apply(Pdf(3), Stamp(), 7, new StampOptions());

        using var document = Reopen(stamped);

        Assert.Equal(3, document.PageCount);
        Assert.True(stamped.Length > 0);
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
    [InlineData("Conclusions.docx", false)]
    [InlineData("Courriel du 3 mars.msg", false)]
    [InlineData("Scan.jpg", false)]
    public void SaysWhatItCanStamp(string fileName, bool expected) =>
        Assert.Equal(expected, PieceStamp.CanStamp(fileName));

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
}
