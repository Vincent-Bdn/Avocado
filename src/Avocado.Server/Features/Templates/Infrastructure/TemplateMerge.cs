using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Avocado.Server.Features.Templates.Infrastructure;

/// <summary>
/// Replaces <c>{{placeholders}}</c> in a .docx with the dossier's own wording.
///
/// <para><b>The hard part is not the substitution.</b> Word splits a paragraph into runs whenever
/// anything changes (a spell-check mark, a language tag, an edit made on a different day), so
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
    public static byte[] Fill(Stream template, IReadOnlyDictionary<string, string> values) =>
        Fill(template, values, repeated: null, prefix: null, optional: null);

    /// <summary>
    /// Fills the template, repeating one table row per entry in <paramref name="repeated"/>.
    ///
    /// <para><b>Why a repeating row rather than a table Avocado builds.</b> The détail that goes out
    /// with a facture is read by a client and sometimes by a juge taxateur, and every cabinet's is
    /// different: its own letterhead, its own column widths, its own closing sentence. A table built
    /// here is Avocado's table, and the next cabinet wants another one. So she gives Avocado the
    /// document she already sends, marks the row that repeats by putting <c>{{ligne.…}}</c> in it,
    /// and that row is cloned once per diligence. The logo, the fonts and the wording stay hers, and
    /// nothing needs a release to change.</para>
    ///
    /// <para>The row is cloned before the scalars are substituted, so the placeholders in the model
    /// row survive long enough to be copied. The model row is then removed: leaving it would print an
    /// empty line with « {{ligne.date}} » in it under every détail.</para>
    /// </summary>
    /// <param name="optional">
    /// Repères whose absence makes their whole line meaningless. A paragraph mentioning one of these
    /// with nothing to put in it is removed rather than filled.
    ///
    /// <para>This exists because « Application d'une remise commerciale de {{remise.taux}} pour
    /// fidélité client » is a sentence about a remise, and with no remise it is not a sentence at
    /// all: filling it printed « Application d'une remise commerciale de  pour fidélité client » on
    /// every détail that had no remise, which went to clients. Avocado cannot tell from the
    /// surrounding French that the line is conditional, and asking her to rewrite her document with
    /// a special marker is asking her to learn a templating language. Naming the few repères that are
    /// genuinely optional costs nothing and works on the document she already wrote.</para>
    ///
    /// <para>Which is why the set has to stay small and deliberate. <c>{{taux}}</c> is not in it,
    /// although it too can be empty: it sits inside the TOTAL line, and dropping that line because
    /// two diligences carried different rates would remove the figure the whole page is about.</para>
    /// </param>
    public static byte[] Fill(
        Stream template,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? repeated,
        string? prefix,
        IReadOnlySet<string>? optional = null)
    {
        using var buffer = new MemoryStream();
        template.CopyTo(buffer);
        buffer.Position = 0;

        using (var document = WordprocessingDocument.Open(buffer, isEditable: true))
        {
            var main = document.MainDocumentPart;

            if (main?.Document?.Body is { } body)
            {
                if (repeated is not null && prefix is { Length: > 0 })
                {
                    Repeat(body, repeated, prefix);
                }

                Substitute(body, values, optional);
            }

            // Headers and footers are where the letterhead lives, so they matter as much as the body.
            foreach (var header in main?.HeaderParts ?? [])
            {
                if (header.Header is { } part)
                {
                    Substitute(part, values, optional);
                }
            }

            foreach (var footer in main?.FooterParts ?? [])
            {
                if (footer.Footer is { } part)
                {
                    Substitute(part, values, optional);
                }
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Clones every table row that mentions <c>{{prefix.…}}</c>, once per entry.
    ///
    /// <para>Rows are found before anything is changed, and the model row is removed only after its
    /// copies are in place, so a table with two repeating rows, or a document with two tables, does
    /// not lose track of where it was.</para>
    /// </summary>
    private static void Repeat(
        DocumentFormat.OpenXml.OpenXmlElement root,
        IReadOnlyList<IReadOnlyDictionary<string, string>> entries,
        string prefix)
    {
        var marker = "{{" + prefix + ".";

        var models = root.Descendants<TableRow>()
            .Where(row => string.Concat(row.Descendants<Text>().Select(text => text.Text))
                .Contains(marker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var model in models)
        {
            foreach (var entry in entries)
            {
                var copy = (TableRow)model.CloneNode(deep: true);
                Substitute(copy, entry, optional: null);

                model.Parent?.InsertBefore(copy, model);
            }

            // Removed last. An empty détail therefore prints its table with no rows rather than one
            // row reading « {{ligne.date}} », which is what she would have sent to the client.
            model.Remove();
        }
    }

    private static void Substitute(
        DocumentFormat.OpenXml.OpenXmlElement root,
        IReadOnlyDictionary<string, string> values,
        IReadOnlySet<string>? optional)
    {
        // Materialised, because a paragraph that fills to nothing is removed as we go.
        foreach (var paragraph in root.Descendants<Paragraph>().ToList())
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

            // A line that was nothing but repères, and whose repères all came out empty, says
            // nothing and is removed rather than printed blank.
            //
            // This is what makes an optional sentence possible at all. « Application d'une remise
            // commerciale de {{remise.phrase}} » on its own line disappears when there is no remise,
            // where filling it left « Application d'une remise commerciale de  » on every détail:
            // a sentence with a hole in it, sent to a client.
            //
            // Only in the body. A table cell must contain at least one paragraph, and a document
            // whose cell holds none is one Word refuses to open.
            // Either the line said nothing but repères and they all came out empty, or it mentions
            // a repère whose absence makes the whole line meaningless. See the `optional` parameter.
            var meaningless =
                (filled.Trim().Length == 0 && OnlyPlaceholders(original))
                || Mentions(original, optional, values);

            // Only in the body. A table cell must contain at least one paragraph, and a document
            // whose cell holds none is one Word refuses to open.
            if (meaningless && paragraph.Parent is Body)
            {
                paragraph.Remove();
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

    /// <summary>Whether the line leans on an optional repère that has nothing to say.</summary>
    private static bool Mentions(
        string original,
        IReadOnlySet<string>? optional,
        IReadOnlyDictionary<string, string> values)
    {
        if (optional is null || optional.Count == 0)
        {
            return false;
        }

        foreach (Match match in Placeholder().Matches(original))
        {
            var field = match.Groups[1].Value;

            if (optional.Contains(field)
                && (!values.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the line held repères and nothing else worth keeping.</summary>
    private static bool OnlyPlaceholders(string text) =>
        Placeholder().Replace(text, string.Empty).Trim().Length == 0;

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
