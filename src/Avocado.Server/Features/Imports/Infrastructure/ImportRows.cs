namespace Avocado.Server.Features.Imports.Infrastructure;

/// <param name="Role">In Gestisoft's own words: Client, Partie adverse, Avocat de la partie adverse.</param>
public sealed record ImportParty(
    string Name,
    string? Role,
    bool IsClient,
    bool IsOrganisation,
    string? Email,
    string? Phone,
    string? Address);

/// <param name="IsDisbursement">Money advanced on the matter rather than received, which is what decides the sign.</param>
public sealed record ImportMovement(
    bool IsInvoice,
    DateOnly Date,
    long AmountCents,
    string? Reference,
    bool IsPaid,
    DateOnly? PaidOn,
    bool IsDisbursement,
    string Label);

/// <summary>
/// What a scanned dossier should carry, gathered from its sources and reconciled, before anything is
/// written to a vault.
///
/// <para>Separate from <see cref="GestisoftImporter"/> because deciding <em>what</em> a dossier owes
/// and who is party to it is where this can be wrong in a way nobody notices, while writing rows is
/// not. Here it can be read, and tested, without a vault.</para>
/// </summary>
public static class ImportRows
{
    /// <summary>
    /// Every party this dossier has a real source for, best source first.
    ///
    /// <para>Gestisoft's own contacts list, where she printed one beside the dossier: it names the
    /// client, the interlocutors, the adverse parties, both sides' avocats and the tribunal, each with
    /// its role already written out. Then avocado-tiers.csv, which she fills in for the dossiers
    /// Gestisoft refused to export. A name in both is taken from the list, since that is the one that
    /// was not retyped.</para>
    /// </summary>
    public static IReadOnlyList<ImportParty> Parties(
        ImportCandidate candidate,
        ContactsList? contacts,
        Sidecars sidecars)
    {
        var parties = new List<ImportParty>();

        foreach (var contact in contacts?.Contacts ?? [])
        {
            parties.Add(new ImportParty(
                contact.Name,
                contact.Role,
                contact.IsClient,
                contact.IsOrganisation,
                contact.Email,
                contact.Phone,
                Blank($"{contact.PostCode} {contact.City}")));
        }

        foreach (var row in sidecars.Tiers.Where(row => Matches(row.Dossier, candidate)))
        {
            if (parties.Any(party => party.Name.Equals(row.Nom, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            parties.Add(new ImportParty(
                row.Nom,
                Blank(row.Role),
                IsClient(row.Role),
                row.Type.StartsWith("PM", StringComparison.OrdinalIgnoreCase)
                    || row.Type.StartsWith("mor", StringComparison.OrdinalIgnoreCase),
                Blank(row.Email),
                Blank(row.Telephone),
                Blank(row.Adresse)));
        }

        return parties;
    }

    /// <summary>
    /// Every facture and movement this dossier has a real source for.
    ///
    /// <para><b>A règlement is not recorded twice.</b> Gestisoft prints the facture and the payment
    /// that settles it as two rows sharing a pointage; entering the payment as a ledger movement as
    /// well as marking the facture paid would count the money twice, which is the one way this model
    /// produces a total that is wrong while looking entirely plausible. So a payment whose pointage
    /// belongs to a facture settles it, and only a payment belonging to none becomes a movement.</para>
    ///
    /// <para>Whether a facture is paid comes from its own solde rather than from the existence of a
    /// payment: one of hers was invoiced at 7 581 €, part paid at 5 000 € and carries 2 581 € still
    /// outstanding, and counting it settled would quietly lose the balance she is owed.</para>
    /// </summary>
    public static IReadOnlyList<ImportMovement> Money(ImportCandidate candidate, Sidecars sidecars)
    {
        var money = new List<ImportMovement>();
        var rows = candidate.BillingFile is { } file ? GestisoftBilling.Read(file) : [];

        var payments = rows
            .Where(row => row.IsPayment && row.Pointage is { Length: > 0 })
            .ToLookup(row => row.Pointage!, StringComparer.Ordinal);

        var invoiced = rows
            .Where(row => row.IsInvoice && row.Pointage is { Length: > 0 })
            .Select(row => row.Pointage!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in rows.Where(row => row.IsInvoice))
        {
            if (row.Date is not { } date)
            {
                continue;
            }

            var settlements = row.Pointage is { Length: > 0 } ? payments[row.Pointage].ToList() : [];

            money.Add(new ImportMovement(
                true,
                date,
                row.ExclVatCents != 0 ? row.ExclVatCents : row.DebitCents,
                row.Reference,
                row.BalanceCents == 0 && settlements.Count > 0,
                settlements.Count > 0 ? settlements.Max(payment => payment.Date) : null,
                false,
                row.Label ?? "Facture"));
        }

        foreach (var row in rows.Where(row =>
            row.IsPayment && (row.Pointage is not { Length: > 0 } || !invoiced.Contains(row.Pointage))))
        {
            if (row.Date is { } date)
            {
                money.Add(new ImportMovement(
                    false, date, row.CreditCents, null, false, null, false, row.Label ?? "Règlement"));
            }
        }

        foreach (var row in sidecars.Facturation.Where(row => Matches(row.Dossier, candidate)))
        {
            var kind = row.Type.ToLowerInvariant();

            if (kind.StartsWith("fact", StringComparison.Ordinal))
            {
                // Already there under the same number, because she typed it in as well as exporting it.
                if (Blank(row.Reference) is { } reference
                    && money.Any(entry => reference.Equals(entry.Reference, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                money.Add(new ImportMovement(
                    true,
                    row.Date,
                    Math.Abs(row.AmountCents),
                    Blank(row.Reference),
                    row.Paye,
                    row.Paye ? row.Date : null,
                    false,
                    Blank(row.Libelle) ?? "Facture"));

                continue;
            }

            // The spreadsheet says which direction in words, and the sign is derived from that rather
            // than read from the number: a stray minus in Excel must not turn a payment received into
            // money advanced.
            money.Add(new ImportMovement(
                false,
                row.Date,
                Math.Abs(row.AmountCents),
                null,
                false,
                null,
                kind.Contains("bours", StringComparison.Ordinal) || kind.StartsWith("deb", StringComparison.Ordinal),
                Blank(row.Libelle) ?? "Repris de Gestisoft"));
        }

        return money;
    }

    /// <summary>« Client », « client », « Cliente ». Anything else is a party of some other kind.</summary>
    internal static bool IsClient(string role) =>
        role.TrimStart().StartsWith("client", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a spreadsheet row is about this dossier. Matched on the client folder's name, the only
    /// identifier the export and the spreadsheet share, and on the affaire's name too so that
    /// splitting a client into two dossiers does not orphan the rows she typed against it.
    /// </summary>
    internal static bool Matches(string dossier, ImportCandidate candidate) =>
        dossier.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)
        || dossier.Equals(candidate.Client, StringComparison.OrdinalIgnoreCase);

    internal static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
