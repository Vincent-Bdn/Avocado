using System.IO.Compression;
using System.Text;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Tests;

public class RecoveryCodeFileTests
{
    private static string NewCode()
    {
        using var key = SecretKey.Generate();
        return RecoveryCode.Format(key);
    }

    [Fact]
    public void ReadsTheCodeFromAPlainTextFile()
    {
        var code = NewCode();
        var file = Encoding.UTF8.GetBytes($"Avocado\nClé de récupération\n\n    {code}\n\nÀ conserver.");

        Assert.Equal(code, RecoveryCodeFile.Extract(file));
    }

    /// <summary>
    /// The sheet renders the code as nine chips, so a producer may store each group as its own string
    /// with no separator between them. Stripping punctuation before searching is what covers that.
    /// </summary>
    [Fact]
    public void ReadsTheCodeSplitAcrossNineSeparateRuns()
    {
        var code = NewCode();
        var groups = code.Split('-');

        var page = new StringBuilder();
        foreach (var group in groups)
        {
            page.Append($"({group}) Tj\n");
        }

        Assert.Equal(code, RecoveryCodeFile.Extract(Pdf(page.ToString())));
    }

    [Fact]
    public void ReadsTheCodeFromACompressedPdfStream()
    {
        var code = NewCode();
        Assert.Equal(code, RecoveryCodeFile.Extract(Pdf($"BT /F1 17 Tf ({code}) Tj ET")));
    }

    /// <summary>
    /// The whole reason scanning is safe: a code carries a checksum, so text that merely looks like
    /// one is rejected rather than returned as somebody's key.
    /// </summary>
    [Fact]
    public void RefusesTextThatMerelyLooksLikeACode()
    {
        var fake = string.Join('-', Enumerable.Repeat("ABCDEF", 9));
        var file = Encoding.UTF8.GetBytes($"Reference {fake} end");

        Assert.Null(RecoveryCodeFile.Extract(file));
    }

    [Fact]
    public void FindsNothingInAFileThatHoldsNothing()
    {
        Assert.Null(RecoveryCodeFile.Extract("Facture n° 2026-014, honoraires."u8.ToArray()));
        Assert.Null(RecoveryCodeFile.Extract(ReadOnlySpan<byte>.Empty));
    }

    /// <summary>An image or a font inside the PDF must be skipped, not throw.</summary>
    [Fact]
    public void SurvivesAStreamItCannotInflate()
    {
        var code = NewCode();

        var document = new MemoryStream();
        document.Write("%PDF-1.4\n"u8);
        document.Write("4 0 obj\n<< /Length 8 >>\nstream\n"u8);
        document.Write([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]);
        document.Write("\nendstream\nendobj\n"u8);

        var tail = Pdf($"({code}) Tj");
        document.Write(tail.AsSpan(tail.AsSpan().IndexOf("4 0 obj"u8) is var offset and >= 0 ? offset : 0));

        Assert.Equal(code, RecoveryCodeFile.Extract(document.ToArray()));
    }

    [Fact]
    public void IgnoresAFileTooLargeToBeASheet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avocado-huge-{Guid.NewGuid():N}.bin");

        try
        {
            using (var file = File.Create(path))
            {
                file.SetLength(RecoveryCodeFile.MaximumBytes + 1);
            }

            Assert.Null(RecoveryCodeFile.Extract(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A minimal PDF whose single content stream is Flate-compressed, as Chromium emits.</summary>
    private static byte[] Pdf(string content)
    {
        var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(Encoding.Latin1.GetBytes(content));
        }

        var document = new MemoryStream();
        document.Write("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n"u8);
        document.Write("4 0 obj\n<< /Filter /FlateDecode >>\nstream\n"u8);
        document.Write(compressed.ToArray());
        document.Write("\nendstream\nendobj\n%%EOF"u8);

        return document.ToArray();
    }
}
