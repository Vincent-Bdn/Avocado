using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Avocado.Server.Features.Templates.Infrastructure;

/// <summary>
/// Replaces <c>{{placeholders}}</c> in a .docx with the dossier's own wording.
///
/// <para><b>The hard part is not the substitution.</b> Word splits a paragraph into runs whenever
/// anything changes — a spell-check mark, a language tag, an edit made on a different day — so
/// « {{client.nom}} » typed in one go is very often stored as « {{clie », « nt.no », « m}} » across
/// three runs. Searching run by run finds nothing and silently leaves the placeholder in the letter.
/// So the text of each paragraph is flattened, substituted, and written back into its first run with
/// the others emptied: the paragraph keeps its style, and the placeholder is found however Word
/// happened to slice it.</para>
/// </summary>
public static partial class TemplateMerge
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>Every placeholder a template uses, so the UI can say which ones it will fill.</summary>
    public static IReadOnlyList<string> Discover(Stream template)
    {
        using var document = WordprocessingDocument.Open(template, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;

        if (body is null)
        {
            return [];
        }

        return
        [
            .. body.Descendants<Paragraph>()
                .SelectMany(paragraph => Placeholder().Matches(TextOf(paragraph)).Select(m => m.Groups[1].Value))
                .Distinct()
                .Order(),
        ];
    }

    /// <summary>
    /// Fills the template and returns the resulting .docx. An unknown placeholder is left exactly as
    /// written rather than blanked: a letter with « {{client.siret}} » still in it is obviously wrong,
    /// where a letter with a silent blank looks finished and is not.
    /// </summary>
    public static byte[] Fill(Stream template, IReadOnlyDictionary<string, string> values)
    {
        using var buffer = new MemoryStream();
        template.CopyTo(buffer);
        buffer.Position = 0;

        using (var document = WordprocessingDocument.Open(buffer, isEditable: true))
        {
            var main = document.MainDocumentPart;

            if (main?.Document?.Body is { } body)
            {
                Substitute(body, values);
            }

            // Headers and footers are where the letterhead lives, so they matter as much as the body.
            foreach (var header in main?.HeaderParts ?? [])
            {
                if (header.Header is { } part)
                {
                    Substitute(part, values);
                }
            }

            foreach (var footer in main?.FooterParts ?? [])
            {
                if (footer.Footer is { } part)
                {
                    Substitute(part, values);
                }
            }
        }

        return buffer.ToArray();
    }

    private static void Substitute(DocumentFormat.OpenXml.OpenXmlElement root, IReadOnlyDictionary<string, string> values)
    {
        foreach (var paragraph in root.Descendants<Paragraph>())
        {
            var original = TextOf(paragraph);

            if (!Placeholder().IsMatch(original))
            {
                continue;
            }

            var filled = Placeholder().Replace(
                original,
                match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

            if (filled == original)
            {
                continue;
            }

            var runs = paragraph.Descendants<Run>().ToList();

            if (runs.Count == 0)
            {
                continue;
            }

            // The first run keeps the paragraph's formatting and takes the whole filled text; the
            // rest are emptied rather than removed, so nothing else in the paragraph shifts.
            SetText(runs[0], filled);

            foreach (var run in runs.Skip(1))
            {
                SetText(run, string.Empty);
            }
        }
    }

    private static string TextOf(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants<Text>().Select(text => text.Text));

    private static void SetText(Run run, string value)
    {
        foreach (var extra in run.Descendants<Text>().Skip(1).ToList())
        {
            extra.Remove();
        }

        var text = run.Descendants<Text>().FirstOrDefault();

        if (text is null)
        {
            text = new Text();
            run.AppendChild(text);
        }

        text.Text = value;
        // Without this a leading or trailing space in the filled value is silently dropped by Word.
        text.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
    }
}
