using Avocado.Server.Data;
using Avocado.Server.Features.Matters.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

public static class UpdateMatter
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        MatterInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["matter"] = [error] });
        }

        var matter = await database.Matters
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (matter is null)
        {
            return Results.NotFound();
        }

        var reference = input.Reference?.Trim();
        if (!string.IsNullOrEmpty(reference) && reference != matter.Reference)
        {
            if (await database.Matters.AnyAsync(
                    other => other.Reference == reference && other.Id != id, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["reference"] = [$"La référence « {reference} » est déjà utilisée."],
                });
            }

            matter.Reference = reference;
        }

        matter.Name = input.Name.Trim();
        matter.Description = input.Description;
        matter.CourtCaseNumber = CreateMatter.Trimmed(input.CourtCaseNumber);
        matter.Classification = CreateMatter.Trimmed(input.Classification);
        matter.Court = CreateMatter.Trimmed(input.Court);
        matter.IsFavourite = input.IsFavourite;

        // Unlike the taux horaire, this reprices nothing behind it: it is the price of the facture
        // she has not issued yet, so changing it is simply changing her mind about a number.
        matter.FixedFeeCents = input.FixedFeeCents;

        if (input.OpenedOn is { } openedOn)
        {
            matter.OpenedOn = openedOn;
        }

        await ApplyRateAsync(database, matter, input.HourlyRateCents, cancellationToken).ConfigureAwait(false);

        matter.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Changes the dossier's rate without repricing an hour of what is already recorded.
    ///
    /// <para><b>This used to refuse outright</b>, with a comment explaining that a retroactive change
    /// would reprice every entry that fell back to the dossier's rate. The reasoning was right and the
    /// behaviour was wrong: the form went on offering an editable field, the value went up to the
    /// server on every save, and the server dropped it on the floor. « La modif du taux horaire ne
    /// fonctionne pas » is what that looks like from a desk.</para>
    ///
    /// <para>So the edit is honoured, and the history it would have damaged is pinned down first:
    /// every entry that was relying on the old rate is given that rate explicitly, as its own
    /// dérogation, before the new one is written. Nothing changes value. An hour worked at 200 € was
    /// worked at 200 € whatever she charges from tomorrow, which is also what a convention
    /// d'honoraires says, and repricing it would quietly alter invoices already sent.</para>
    ///
    /// <para>Entries that already carried a dérogation are left alone: they were never relying on
    /// the dossier's rate, so there is nothing to pin.</para>
    /// </summary>
    private static async Task ApplyRateAsync(
        AvocadoDbContext database,
        Matter matter,
        long? requested,
        CancellationToken cancellationToken)
    {
        if (requested is not { } rate || rate <= 0 || rate == matter.HourlyRateCents)
        {
            return;
        }

        var previous = matter.HourlyRateCents;

        await database.TimeEntries
            .Where(entry => entry.MatterId == matter.Id && entry.HourlyRateCentsOverride == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(entry => entry.HourlyRateCentsOverride, previous),
                cancellationToken)
            .ConfigureAwait(false);

        matter.HourlyRateCents = rate;
    }
}
