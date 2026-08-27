using System.Text.RegularExpressions;

namespace Avocado.Server.Features.Imports.Infrastructure;

/// <param name="Role">In Gestisoft's own words: Client, Partie adverse, Avocat de la partie adverse.</param>
/// <param name="IsClient">The one role Avocado treats specially, since a dossier has exactly one.</param>
public sealed record GestisoftContact(
    string Name,
    string Role,
    bool IsClient,
    bool IsOrganisation,
    string? PostCode,
    string? City,
    string? Email,
    string? Phone);

/// <param name="Code">« 700978 ». The number that also heads the billing export.</param>
public sealed record ContactsList(string? Code, string? Label, IReadOnlyList<GestisoftContact> Contacts);

/// <summary>
/// Reads « Liste des contacts », the report Gestisoft prints beside a dossier.
///
/// <para>It is a proper source and was being thrown away: the importer was inventing a single client
/// from the folder's name while a file two directories along named the client, the interlocutor, the
/// opposing party, both of their avocats and the tribunal, each with its role already written
/// out.</para>
///
/// <para><b>Structure is indentation, meaning is vocabulary.</b> Three spaces per level, and a short
/// list of headings that Gestisoft always writes the same way. A line is a heading when it is one of
/// those, a detail when it carries a marker like (E) or (Tp), and a contact otherwise. Relying on the
/// depth alone would break on the nesting that does occur, an avocat under an adverse party under a
/// heading, five levels down.</para>
/// </summary>
public static class GestisoftContacts
{
    /// <summary>The headings, and what each means once it is in a dossier.</summary>
    private static readonly (string Heading, string Role, bool IsClient)[] Headings =
    [
        ("clients", "Client", true),
        ("client", "Client", true),
        ("interlocuteur", "Interlocuteur", false),
        ("partie adverse", "Partie adverse", false),
        ("avocat de la partie adverse", "Avocat de la partie adverse", false),
        ("avocat correspondant pour l'affaire", "Avocat correspondant", false),
        ("avocat correspondant", "Avocat correspondant", false),
        ("juridiction", "Juridiction", false),
        ("expert", "Expert", false),
        ("huissier", "Huissier", false),
        ("commissaire de justice", "Commissaire de justice", false),
        ("autres tiers intervenant", "Tiers intervenant", false),
        ("autre tiers intervenant", "Tiers intervenant", false),
        ("tiers intervenant", "Tiers intervenant", false),
        ("mandataire", "Mandataire", false),
        ("notaire", "Notaire", false),
    ];

    /// <summary>« 700978 - TELE MEDECINS DE FRANCE / SILLAND NANCY-ZBO-PPA-05-Commercial-Contrats-- »</summary>
    private static readonly Regex Header = new(@"^\s*(?<code>\d{5,8})\s*-\s*(?<label>.+?)(-[A-Z]{2,4}){2,}", RegexOptions.Compiled);

    /// <summary>A telephone or an email, tagged by Gestisoft with (T), (M), (Tp), (Td), (E), (F).</summary>
    private static readonly Regex Detail = new(@"^\s*(?<value>.*?)\s*\((?<kind>[A-Za-z]{1,3})\)\s*$", RegexOptions.Compiled);

    /// <summary>A name, then a French postcode, then a town, then whatever qualifier follows a colon.</summary>
    private static readonly Regex Located = new(@"^(?<name>.*?)\s+(?<cp>\d{5})\s+(?<city>[^:]*?)\s*(:.*)?$", RegexOptions.Compiled);

    public static ContactsList? Read(string path)
    {
        var lines = PdfLines.Read(path);

        if (lines.Count == 0 || !lines.Any(line => line.Contains("Liste des contacts", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string? code = null;
        string? label = null;
        var contacts = new List<GestisoftContact>();

        // Headings nest, and their scope ends when the indentation comes back out. « Avocat de la
        // partie adverse » sits under one adverse party at twelve spaces; the next party appears again
        // at six and is a party, not another avocat. Reading the vocabulary alone gave every later
        // adverse party the avocat's role.
        var scope = new Stack<(int Indent, string Role, bool IsClient)>();
        GestisoftContact? current = null;

        foreach (var line in lines)
        {
            var text = line.TrimEnd();
            var trimmed = text.Trim();

            if (trimmed.Length == 0 || IsFurniture(trimmed))
            {
                continue;
            }

            if (code is null && Header.Match(trimmed) is { Success: true } header)
            {
                code = header.Groups["code"].Value;
                label = header.Groups["label"].Value.Replace("  ", " ").Trim();
                continue;
            }

            var indent = text.Length - text.TrimStart().Length;

            while (scope.Count > 0 && scope.Peek().Indent >= indent)
            {
                scope.Pop();
            }

            if (Match(trimmed) is { } heading)
            {
                scope.Push((indent, heading.Role, heading.IsClient));
                current = null;
                continue;
            }

            // A detail belongs to whatever contact was named above it. Gestisoft prints the telephone
            // and the address on their own lines and never repeats the name.
            if (Detail.Match(text) is { Success: true } detail && current is not null)
            {
                var value = detail.Groups["value"].Value.Trim();
                var kind = detail.Groups["kind"].Value.ToLowerInvariant();

                if (value.Length == 0)
                {
                    continue;
                }

                var index = contacts.LastIndexOf(current);

                current = kind == "e"
                    ? current with { Email = current.Email ?? value }
                    : current with { Phone = current.Phone ?? value };

                if (index >= 0)
                {
                    contacts[index] = current;
                }

                continue;
            }

            var (role, isClient) = scope.Count > 0
                ? (scope.Peek().Role, scope.Peek().IsClient)
                : ("Partie", false);

            if (Parse(trimmed, role, isClient) is { } contact)
            {
                current = contact;
                contacts.Add(contact);
            }
        }

        return new ContactsList(code, label, contacts);
    }

    private static (string Role, bool IsClient)? Match(string line)
    {
        var lower = line.ToLowerInvariant().TrimEnd(':', ' ');

        // « Interlocuteur client », « Interlocuteur Avocat de la partie adverse ». The word introduces
        // the people inside whatever came before it, whatever that was.
        if (lower.StartsWith("interlocuteur", StringComparison.Ordinal))
        {
            return (line.Trim(), false);
        }


        foreach (var (heading, role, isClient) in Headings)
        {
            // StartsWith, because Gestisoft writes « Partie adverse : adversaires ou autres parties
            // appelées » and « Juridiction 1ère Instance », and the tail carries nothing we need.
            if (lower.StartsWith(heading, StringComparison.Ordinal))
            {
                return (role, isClient);
            }
        }

        return null;
    }

    private static GestisoftContact? Parse(string line, string role, bool isClient)
    {
        // « Références 1 : 2025162 SM/SM » is the other side's file number, not a person.
        if (line.StartsWith("Référence", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string name;
        string? postCode = null;
        string? city = null;

        if (Located.Match(line) is { Success: true } located)
        {
            name = located.Groups["name"].Value;
            postCode = located.Groups["cp"].Value;
            city = located.Groups["city"].Value.Trim();
        }
        else
        {
            // « THOMAS Jennifer : Avocat / », « ORADIANSE - Mme DESRUES Laura »: no address given.
            var colon = line.IndexOf(" : ", StringComparison.Ordinal);
            name = colon > 0 ? line[..colon] : line;
        }

        // « Tribunal de Commerce - Ressort : 33000 BORDEAUX » puts the colon before the postcode, and
        // « - Ressort » is the column Gestisoft prints the jurisdiction's reach in, left empty here.
        name = name.TrimEnd().TrimEnd(':', '-', ' ');
        name = Regex.Replace(name, @"\s*-\s*Ressort$", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s{2,}", " ").Trim();

        city = Detown(city);

        return name.Length < 2 ? null : new GestisoftContact(
            name, role, isClient, LooksLikeOrganisation(name), postCode, NullIfBlank(city), null, null);
    }

    /// <summary>
    /// The town, with the telephone number that sometimes trails it taken off.
    ///
    /// <para>Gestisoft prints « ALLIANCE DIFFUSION   33150 CENON 05 56 06 23 39 » on one line, so the
    /// town runs into the number. Nine digits or more is the test, which leaves « MARSEILLE 08 » and
    /// « CEDEX 2 » alone.</para>
    /// </summary>
    private static string? Detown(string? city)
    {
        if (city is null)
        {
            return null;
        }

        var trimmed = Regex.Replace(city, @"\s*\d[\d\s.]{8,}$", match =>
            match.Value.Count(char.IsAsciiDigit) >= 9 ? string.Empty : match.Value).Trim();

        return trimmed.Length == 0 ? city.Trim() : trimmed;
    }

    /// <summary>
    /// Whether to file this as a personne morale.
    ///
    /// <para>« SEURIN Héléne » and « JANKIEWICZ Stéphanie » are people: a surname in capitals followed
    /// by a given name that is not. « TELE MEDECINS DE FRANCE », « CABINET PALMIER BRAULT ASSOCIEES
    /// AARPI » and « SELARL FORZY - BOCHE ANNIC - MICHON » are not, and neither is « Tribunal de
    /// Commerce ». Wrong occasionally, and a fiche with the wrong type is a click to fix, where a
    /// missing one is an evening of retyping.</para>
    /// </summary>
    private static bool LooksLikeOrganisation(string name)
    {
        if (name.Contains("Mme", StringComparison.Ordinal) || name.Contains("M.", StringComparison.Ordinal))
        {
            return false;
        }

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // A capitalised word that is not all capitals, after at least one that is: a given name.
        for (var index = 1; index < words.Length; index++)
        {
            var word = words[index];

            if (word.Length > 1
                && char.IsUpper(word[0])
                && word.Skip(1).Any(char.IsLower)
                && words[index - 1].Length > 1
                && words[index - 1].All(character => !char.IsLetter(character) || char.IsUpper(character)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFurniture(string line) =>
        line.StartsWith("Liste des contacts", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("En Date du", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("GestiSoft", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Page N", StringComparison.OrdinalIgnoreCase);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
