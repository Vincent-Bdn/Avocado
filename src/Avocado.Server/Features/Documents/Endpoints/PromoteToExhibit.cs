using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Documents.Endpoints;

/// <param name="ExhibitNumber">Omit to take the next free number.</param>
/// <param name="ExhibitLabel">
/// Written for the judge — « Bail commercial du local sis 14 rue Duquesne, Lyon 6ᵉ, du 1ᵉʳ mars 2019 »,
/// never the file name.
/// </param>
public sealed record ExhibitInput(string ExhibitLabel, int? ExhibitNumber);

/// <summary>
/// « Verser comme pièce » — the button names the legal act, not the storage operation. Nothing moves:
/// the document stays where it is and gains a number and a libellé.
/// </summary>
public static class PromoteToExhibit
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        ExhibitInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.ExhibitLabel))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["exhibitLabel"] = ["Le libellé de pièce est obligatoire — il est cité dans les conclusions."],
            });
        }

        var document = await database.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (document is null)
        {
            return Results.NotFound();
        }

        var used = await database.Documents
            .Where(other => other.MatterId == document.MatterId
                            && other.ExhibitNumber != null
                            && other.Id != id)
            .Select(other => other.ExhibitNumber!.Value)
            .OrderBy(number => number)
            .ToListAsync(cancellationToken);

        var number = input.ExhibitNumber ?? document.ExhibitNumber ?? ListDocuments.NextNumber(used);

        if (number < 1)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["exhibitNumber"] = ["Un numéro de pièce commence à 1."],
            });
        }

        // The unique index would catch this, but a foreign-key style error tells her nothing about
        // which document already holds the number.
        if (used.Contains(number))
        {
            return Results.Problem(
                title: "Numéro déjà utilisé",
                detail: $"La pièce n° {number} existe déjà dans ce dossier.",
                statusCode: StatusCodes.Status409Conflict);
        }

        document.ExhibitNumber = number;
        document.ExhibitLabel = input.ExhibitLabel.Trim();

        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { document.Id, document.ExhibitNumber, document.ExhibitLabel });
    }
}
