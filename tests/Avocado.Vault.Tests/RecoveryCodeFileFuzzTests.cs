using System.IO.Compression;
using System.Text;
using Avocado.Vault.Crypto;

namespace Avocado.Vault.Tests;

/// <summary>
/// The scanner returns whichever candidate parses first, and a recovery code's checksum is two
/// characters: ten bits, so about one window in a thousand accepts by chance. A single green run of
/// the ordinary tests therefore proves very little, which is exactly how a false positive reached
/// main and only appeared on one macOS run, on one randomly generated key.
///
/// <para>Two hundred keys per case, so a fault at that rate shows up here rather than in CI on a day
/// somebody is trying to ship.</para>
/// </summary>
public class RecoveryCodeFileFuzzTests
{
    private const int Rounds = 200;

    [Fact]
    public void ReadsBackEveryKeyItWritesIntoACompressedPdf()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var code = NewCode();

            Assert.Equal(code, RecoveryCodeFile.Extract(Pdf($"BT /F1 17 Tf ({code}) Tj ET")));
        }
    }

    /// <summary>
    /// The case that failed: nine separate text runs, no separators between them, so the condensed
    /// fallback has to do the work and the compressed bytes must not be in the haystack.
    /// </summary>
    [Fact]
    public void ReadsBackEveryKeySplitAcrossNineRuns()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var code = NewCode();

            var page = new StringBuilder();
            foreach (var group in code.Split('-'))
            {
                page.Append($"({group}) Tj\n");
            }

            Assert.Equal(code, RecoveryCodeFile.Extract(Pdf(page.ToString())));
        }
    }

    /// <summary>
    /// A PDF holding no code at all must keep returning nothing, however many times it is asked. This
    /// is the direction that matters most: returning a key that was never on the page sends someone
    /// to « clé refusée » with no idea why.
    /// </summary>
    [Fact]
    public void NeverInventsAKeyFromADocumentThatHasNone()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var noise = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(400));

            Assert.Null(RecoveryCodeFile.Extract(Pdf($"BT /F1 12 Tf (Facture n {round}) Tj ({noise}) Tj ET")));
        }
    }

    private static string NewCode()
    {
        using var key = SecretKey.Generate();
        return RecoveryCode.Format(key);
    }

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
