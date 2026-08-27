using System.IO.Compression;
using System.Text;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <summary>
/// The lines of text in a PDF, with their leading spaces intact.
///
/// <para>Not a PDF library and not on the way to becoming one. Gestisoft's contact list is a report
/// printed to PDF: every line is one literal string handed to a text operator, and the indentation
/// that carries the whole structure, three spaces per level, is inside those strings. Pulling the
/// literals out in order is enough to get the report back, and anything more general would be a great
/// deal of work to read one known document.</para>
///
/// <para>Uncompressed content is handled as well as Flate, because hers is uncompressed and other
/// printers are not.</para>
/// </summary>
public static class PdfLines
{
    public static IReadOnlyList<string> Read(string path)
    {
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        // Latin-1, and it has to be: a PDF's bytes are not text, and this must be a lossless
        // byte-to-char mapping so that stream boundaries survive. UTF-8 would replace every invalid
        // sequence and corrupt the offsets before anything could be read.
        var raw = Encoding.Latin1.GetString(bytes);
        var lines = new List<string>();

        var cursor = 0;
        while (true)
        {
            var open = raw.IndexOf("stream", cursor, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            Collect(raw[cursor..open], lines);

            var body = open + "stream".Length;
            if (body < raw.Length && raw[body] == '\r') body++;
            if (body < raw.Length && raw[body] == '\n') body++;

            var close = raw.IndexOf("endstream", body, StringComparison.Ordinal);
            if (close < 0)
            {
                cursor = body;
                break;
            }

            Collect(Inflate(raw[body..close]) ?? raw[body..close], lines);
            cursor = close + "endstream".Length;
        }

        Collect(raw[cursor..], lines);
        return lines;
    }

    /// <summary>
    /// Every literal string given to a text-showing operator, in the order it appears.
    ///
    /// <para>The operator has to be checked. Taking every bracketed run in the file also collects the
    /// document information dictionary, so a contact list came back with « QuickReports PDF Export »
    /// and the export timestamp filed as parties to a matter.</para>
    /// </summary>
    private static void Collect(string content, List<string> lines)
    {
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != '(')
            {
                continue;
            }

            var text = new StringBuilder();
            var depth = 1;
            index++;

            for (; index < content.Length && depth > 0; index++)
            {
                var character = content[index];

                if (character == '\\' && index + 1 < content.Length)
                {
                    // An escaped bracket is content. The others matter little in a printed report.
                    text.Append(content[++index] switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        var escaped => escaped,
                    });

                    continue;
                }

                if (character == '(')
                {
                    depth++;
                }
                else if (character == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }

                text.Append(character);
            }

            var line = text.ToString();

            if (line.Trim().Length > 0 && IsShown(content, index))
            {
                lines.Add(line);
            }
        }
    }

    /// <summary>Whether a text operator follows the literal that just ended at <paramref name="index"/>.</summary>
    private static bool IsShown(string content, int index)
    {
        var cursor = index + 1;

        while (cursor < content.Length && char.IsWhiteSpace(content[cursor]))
        {
            cursor++;
        }

        if (cursor >= content.Length)
        {
            return false;
        }

        // Tj and TJ show a string; ' and " show one and move to the next line first.
        return content[cursor] is '\'' or '"'
            || (content[cursor] == 'T' && cursor + 1 < content.Length && content[cursor + 1] is 'j' or 'J');
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
            return Encoding.Latin1.GetString(destination.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
