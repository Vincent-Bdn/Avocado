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
        matter.CourtCaseNumber = string.IsNullOrWhiteSpace(input.CourtCaseNumber)
            ? null
            : input.CourtCaseNumber.Trim();

        if (input.OpenedOn is { } openedOn)
        {
            matter.OpenedOn = openedOn;
        }

        // The rate is deliberately not updated here. Changing it retroactively would reprice every
        // time entry that fell back to it; a rate change belongs on new matters or on an entry's
        // own override.
        matter.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
