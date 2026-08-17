using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text;

namespace Avocado.Vault.Crypto;

/// <summary>
/// Finds a recovery key inside a file the user hands over: the printed sheet saved as a PDF, or the
/// text file the wizard writes to a USB key.
///
/// <para>Typing fifty-four characters off paper is fine when the paper is all there is. When the file
/// is right there, asking for them anyway is asking someone to transcribe a number a computer can
/// read, on the morning after they lost a computer. This is the second recovery path, and it is meant
/// to be the one most people use.</para>
///
/// <para><b>Why the search is bounded rather than exhaustive.</b> A recovery code carries a checksum,
/// which is what lets a candidate be tested rather than merely parsed. It is two characters though,
/// ten bits, so it turns away about 1023 wrong answers in 1024 and no more. Trying every position in
/// a document would therefore find a valid-looking code in noise soon enough, and did. The search
/// looks only where a code could actually be written: nine groups standing on their own.</para>
///
/// <para>Nothing here is a PDF parser and nothing here should become one. Chromium's printToPDF puts
/// the page's text in Flate-compressed content streams as parenthesised literals; inflating those and
/// keeping what is inside the brackets is enough to find nine groups of six. A file it cannot read
/// simply yields nothing, and the user types the code as before.</para>
/// </summary>
public static class RecoveryCodeFile
{
    /// <summary>Guards against being handed a video by mistake. A sheet is a few hundred kilobytes.</summary>
    public const int MaximumBytes = 32 * 1024 * 1024;

    /// <summary>
    /// The recovery code this file contains, or null if it holds none that parses. The returned code
    /// is re-formatted, so what goes on screen looks like the sheet regardless of how it was stored.
    /// </summary>
    public static string? Extract(ReadOnlySpan<byte> content)
    {
        // The signature, not the extension: people rename things, and a PDF saved as .txt should
        // still work.
        var text = content.StartsWith("%PDF"u8)
            ? ExtractPdfText(content)
            : Decode(content);

        return FindCode(text);
    }

    public static string? Extract(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumBytes)
        {
            return null;
        }

        return Extract(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Finds a code in extracted text.
    ///
    /// <para><b>The match has to stand on its own.</b> An earlier version stripped every separator and
    /// slid a 54-character window along, which was the only way it could think of to read a producer
    /// that stores each group as its own string. That made false positives ordinary rather than
    /// impossible: the checksum is two characters, ten bits, so about one window in a thousand accepts
    /// by chance, and eight hundred characters of anything offer eight hundred windows. It duly
    /// returned a key that was never on the page, on one macOS run, on one randomly generated key.</para>
    ///
    /// <para>The pattern below allows zero to three separators between groups, so it reads a code
    /// written with dashes, with spaces, or with nothing at all between the nine. What it will not do
    /// is match inside a longer run of letters and digits: the boundaries either side are what stop a
    /// hash, an identifier or a base64 blob from being read as somebody's recovery key.</para>
    /// </summary>
    private static string? FindCode(string text)
    {
        foreach (Match match in Formatted.Matches(text))
        {
            if (RecoveryCode.TryParse(match.Value, out var key) && key is not null)
            {
                using (key)
                {
                    return RecoveryCode.Format(key);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Nine groups of six, separated by at most three non-alphanumeric characters, and bounded on both
    /// sides so it can never be a slice of something longer.
    /// </summary>
    private static readonly Regex Formatted = new(
        @"(?<![0-9A-Za-z])[0-9A-Za-z]{6}(?:[^0-9A-Za-z]{0,3}[0-9A-Za-z]{6}){8}(?![0-9A-Za-z])",
        RegexOptions.Compiled);

    /// <summary>
    /// Everything between brackets, taken from the parts of the file that are text and from the
    /// streams we can inflate.
    ///
    /// <para>Stream bodies are deliberately skipped on the raw pass. Deflate output is effectively
    /// random bytes, so scanning it for brackets harvests binary noise into the candidate text, and
    /// noise plus a ten-bit checksum is how the scanner once returned a code that was never on the
    /// page. Only what is genuinely text, or has been made into text by inflating it, is considered.</para>
    /// </summary>
    private static string ExtractPdfText(ReadOnlySpan<byte> content)
    {
        var text = new StringBuilder();
        var raw = Decode(content);

        var cursor = 0;
        while (true)
        {
            var open = raw.IndexOf("stream", cursor, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            AppendLiterals(raw[cursor..open], text);

            var body = open + "stream".Length;
            // The specification allows CRLF or LF after the keyword, and nothing else.
            if (body < raw.Length && raw[body] == (char)13) body++;
            if (body < raw.Length && raw[body] == (char)10) body++;

            var close = raw.IndexOf("endstream", body, StringComparison.Ordinal);
            if (close < 0)
            {
                cursor = open + "stream".Length;
                break;
            }

            if (Inflate(raw[body..close]) is { } inflated)
            {
                AppendLiterals(inflated, text);
            }

            cursor = close + "endstream".Length;
        }

        AppendLiterals(raw[cursor..], text);
        return text.ToString();
    }

    private static string? Inflate(string body)
    {
        try
        {
            var bytes = new byte[body.Length];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)body[index];
            }

            using var source = new MemoryStream(bytes);
            using var decompressor = new ZLibStream(source, CompressionMode.Decompress);
            using var destination = new MemoryStream();

            decompressor.CopyTo(destination);
            return Decode(destination.ToArray());
        }
        catch (InvalidDataException)
        {
            // Not Flate, or not a stream we can read. An image, a font. Skip it.
            return null;
        }
    }

    private static void AppendLiterals(string source, StringBuilder text)
    {
        var depth = 0;

        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];

            if (character == '\\' && depth > 0)
            {
                index++; // An escaped bracket is content, not structure.
                continue;
            }

            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character == ')')
            {
                depth = Math.Max(0, depth - 1);

                // A separator, so two adjacent runs cannot fuse into a character sequence that was
                // never on the page.
                text.Append(' ');
                continue;
            }

            if (depth > 0)
            {
                text.Append(character);
            }
        }
    }

    /// <summary>
    /// Latin-1 rather than UTF-8, deliberately. A PDF's bytes are not text, and this has to be a
    /// lossless byte-to-char mapping so that stream offsets survive; UTF-8 would replace every
    /// invalid sequence with U+FFFD and corrupt the compressed data before it could be inflated.
    /// </summary>
    private static string Decode(ReadOnlySpan<byte> content) =>
        Encoding.Latin1.GetString(content);
}
