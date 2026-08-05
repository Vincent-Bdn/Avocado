using Avocado.Server.Data;
using Avocado.Server.Features.Matters.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Matters.Endpoints;

public static class CreateMatter
{
    /// <summary>Fallback when no practice default is configured yet. 280 €/h.</summary>
    private const long DefaultHourlyRateCents = 28_000;

    public static async Task<IResult> HandleAsync(
        MatterInput input,
        AvocadoDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["matter"] = [error] });
        }

        if (!await database.Contacts.AnyAsync(c => c.Id == input.ClientContactId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["clientContactId"] = ["Ce tiers n'existe pas."],
            });
        }

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var reference = input.Reference?.Trim();

        if (string.IsNullOrEmpty(reference))
        {
            reference = await NextReferenceAsync(database, today.Year, cancellationToken);
        }
        else if (await database.Matters.AnyAsync(m => m.Reference == reference, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reference"] = [$"La référence « {reference} » est déjà utilisée."],
            });
        }

        var matter = new Matter
        {
            Reference = reference,
            Name = input.Name.Trim(),
            Description = input.Description,
            OpenedOn = input.OpenedOn ?? today,
            // Frozen here on purpose: changing the practice default later must not reprice this matter.
            HourlyRateCents = input.HourlyRateCents ?? DefaultHourlyRateCents,
            CourtCaseNumber = string.IsNullOrWhiteSpace(input.CourtCaseNumber)
                ? null
                : input.CourtCaseNumber.Trim(),
        };

        matter.Parties.Add(new MatterParty
        {
            ContactId = input.ClientContactId,
            IsClient = true,
            Role = "Client",
        });

        database.Matters.Add(matter);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/matters/{matter.Id}", new { matter.Id, matter.Reference });
    }

    /// <summary>
    /// Next <c>YYYY-NNNN</c> for the year. Derived from the highest existing reference rather than a
    /// stored counter, so importing historical dossiers with their own numbers cannot desynchronise it.
    /// </summary>
    private static async Task<string> NextReferenceAsync(
        AvocadoDbContext database,
        int year,
        CancellationToken cancellationToken)
    {
        var prefix = $"{year}-";

        var existing = await database.Matters
            .Where(matter => matter.Reference.StartsWith(prefix))
            .Select(matter => matter.Reference)
            .ToListAsync(cancellationToken);

        var highest = existing
            .Select(reference => int.TryParse(reference[prefix.Length..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highest + 1:D4}";
    }
}
