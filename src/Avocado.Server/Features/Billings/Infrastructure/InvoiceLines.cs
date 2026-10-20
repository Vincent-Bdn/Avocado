using Avocado.Server.Data;
using Avocado.Server.Features.TimeEntries;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Infrastructure;

/// <summary>
/// The lines a facture takes with it.
///
/// <para><b>Why this is one place.</b> A facture's one structural job is to take time entries out of
/// « reste à facturer » and to record what they were worth on the day it was issued. That was
/// implemented once, in « facturer du temps passé », and the two other ways a facture comes into
/// existence did not do it: dropping the PDF of a facture onto a dossier, and typing one in by hand.
/// Both created a facture that consumed nothing, so the hours went on waiting, « reste à facturer »
/// stayed too high, and the boni/mali was zero because no lines were attached to compare against.</para>
///
/// <para>What that looked like from the other side of the screen: a facture is dropped on PIRBAY,
/// nothing is offered to settle, the hours are still there, so they are soldées en mali — and a mali
/// appears on a dossier that never had one. The figures were wrong in two directions at once.</para>
///
/// <para><b>Reading is separate from attaching</b> because one caller needs to know what the lines
/// are worth before it can decide the amount: « facturer du temps passé » defaults the facture to the
/// value of the selection and lets her override it, and the difference is the arbitrage. The other
/// two already know the amount, it is printed on the facture.</para>
/// </summary>
public static class InvoiceLines
{
    /// <summary>The key every caller reports its validation failure under, so the window can find it.</summary>
    public const string Field = "timeEntries";

    /// <summary>
    /// The lines she chose, checked.
    ///
    /// <para>Three refusals, and each one is a figure that would otherwise go wrong silently: a line
    /// from another dossier would move money between dossiers, a line already attached would be
    /// billed twice, and an id that matches nothing would quietly bill less than she selected.</para>
    ///
    /// <para>An empty selection is not a refusal. A facture may legitimately consume nothing — a
    /// reprise, or a facture whose hours were never recorded here — and that is exactly what
    /// <see cref="BillingInvoice.BilledTimeCents"/> being zero means.</para>
    /// </summary>
    public static async Task<(List<TimeEntry> Lines, string? Error)> ReadAsync(
        AvocadoDbContext database,
        Guid matterId,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return ([], null);
        }

        // Distinct, because the same id sent twice would pass the count check against a list of two
        // and bill one line as if it were two.
        var wanted = ids.Distinct().ToList();

        var lines = await database.TimeEntries
            .Where(entry => entry.MatterId == matterId && wanted.Contains(entry.Id))
            .ToListAsync(cancellationToken);

        if (lines.Count != wanted.Count)
        {
            return ([], "Certaines lignes n'appartiennent pas à ce dossier.");
        }

        if (lines.Any(entry => entry.InvoiceId is not null))
        {
            return ([], "Certaines lignes sont déjà rattachées à une facture.");
        }

        return (lines, null);
    }

    /// <summary>
    /// Hands the lines to the facture and records what they were worth.
    ///
    /// <para>The value is frozen here rather than recomputed on read, because the rate, the entries
    /// and the corrections all move afterwards and the figure she wants is what the arbitrage
    /// actually was on the day she made it. See <see cref="BillingInvoice.BilledTimeCents"/>.</para>
    ///
    /// <para>Returns that value so the caller can use it as the facture's default amount. It does not
    /// save: the facture and its lines are one change and belong in one <c>SaveChangesAsync</c>.</para>
    ///
    /// <para><b>Added to, never replaced.</b> A facture typed in by hand, then given its PDF and its
    /// lines a week later, has to end up worth the sum of both passes. Assigning would have silently
    /// dropped whatever it already carried, which on a facture issued from temps passé is the whole
    /// of its boni or mali. On a new facture the two are the same thing, since it starts at zero.</para>
    /// </summary>
    public static long Attach(
        IReadOnlyList<TimeEntry> lines,
        BillingInvoice invoice,
        long matterHourlyRateCents)
    {
        var worth = Worth(lines, matterHourlyRateCents);

        foreach (var entry in lines)
        {
            entry.InvoiceId = invoice.Id;
        }

        invoice.BilledTimeCents += worth;

        return worth;
    }

    /// <summary>What a selection is worth, without attaching anything. For the amount default.</summary>
    public static long Worth(IReadOnlyList<TimeEntry> lines, long matterHourlyRateCents) =>
        lines.Sum(entry => entry.AmountCents(matterHourlyRateCents));
}
