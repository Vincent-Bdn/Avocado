using System.Buffers;
using System.IO.Compression;
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
/// <para><b>Why scanning is safe rather than reckless.</b> A recovery code carries a checksum, so
/// <see cref="RecoveryCode.TryParse"/> rejects anything that is not one. That turns "find the key in
/// this file" from a parsing problem into a search: pull out every run of alphabet characters and
/// test each candidate. A false positive would have to be a checksum collision produced by accident,
/// which is not a thing that happens.</para>
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
    /// Slides over every run of alphabet characters and asks the parser. Separators are dropped
    /// first, because the sheet renders the code as nine chips and a PDF may store each of them as
    /// its own string with no dashes between them.
    /// </summary>
    private static string? FindCode(string text)
    {
        // The code is 54 characters plus its separators once stripped. Anything shorter cannot be one.
        const int length = 54;

        var condensed = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                condensed.Append(char.ToUpperInvariant(character));
            }
        }

        var candidates = condensed.ToString();

        for (var start = 0; start + length <= candidates.Length; start++)
        {
            if (RecoveryCode.TryParse(candidates.Substring(start, length), out var key) && key is not null)
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
    /// Everything between brackets in every stream we can inflate. Crude on purpose: the goal is to
    /// surface character data, not to understand the document.
    /// </summary>
    private static string ExtractPdfText(ReadOnlySpan<byte> content)
    {
        var text = new StringBuilder();

        // Uncompressed literals first: some producers do not compress at all, and it costs one pass.
        AppendLiterals(Decode(content), text);

        foreach (var stream in InflateStreams(content))
        {
            AppendLiterals(stream, text);
        }

        return text.ToString();
    }

    private static IEnumerable<string> InflateStreams(ReadOnlySpan<byte> content)
    {
        var inflated = new List<string>();
        var raw = Decode(content);

        var cursor = 0;
        while (true)
        {
            var open = raw.IndexOf("stream", cursor, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            var body = open + "stream".Length;

            // The specification allows CRLF or LF after the keyword, and nothing else.
            if (body < raw.Length && raw[body] == '\r') body++;
            if (body < raw.Length && raw[body] == '\n') body++;

            var close = raw.IndexOf("endstream", body, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            cursor = close + "endstream".Length;

            try
            {
                var bytes = new byte[close - body];
                for (var index = 0; index < bytes.Length; index++)
                {
                    bytes[index] = (byte)raw[body + index];
                }

                using var source = new MemoryStream(bytes);
                using var decompressor = new ZLibStream(source, CompressionMode.Decompress);
                using var destination = new MemoryStream();

                decompressor.CopyTo(destination);
                inflated.Add(Decode(destination.ToArray()));
            }
            catch (InvalidDataException)
            {
                // Not Flate, or not a stream we can read. An image, a font. Skip it.
            }
        }

        return inflated;
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
