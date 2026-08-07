using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Billings.Endpoints;

/// <param name="Kind">« Rétrocession d'honoraires », « Sous-traitance »… Free text.</param>
/// <param name="AmountExclVatCents">Always positive: the direction is the type, never the sign.</param>
public sealed record BillingCostInput(
    DateOnly Date,
    string? Kind,
    string Label,
    long AmountExclVatCents,
    Guid? ContactId,
    string? ExternalReference,
    Guid? InvoiceId,
    bool IsPaid = false,
    DateOnly? PaidOn = null)
{
    public string? Validate() => this switch
    {
        { AmountExclVatCents: <= 0 } => "Le montant doit être positif.",
        { Label: var label } when string.IsNullOrWhiteSpace(label) =>
            "Indiquez à quoi correspond cette charge.",
        { IsPaid: false, PaidOn: not null } =>
            "Une charge non réglée ne peut pas avoir de date de règlement.",
        _ => null,
    };
}

/// <summary>
/// Rétrocessions d'honoraires and other sous-traitance. What the dossier cost the cabinet, as opposed
/// to what it advanced for the client, see <see cref="BillingCost"/> for why the two are separate.
/// </summary>
public static class ManageCosts
{
    public static async Task<IResult> AddAsync(
        Guid matterId,
        BillingCostInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["cost"] = [error] });
        }

        if (!await database.Matters.AnyAsync(matter => matter.Id == matterId, cancellationToken))
        {
            return Results.NotFound();
        }

        var cost = new BillingCost { MatterId = matterId };
        Apply(input, cost);

        database.Costs.Add(cost);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/costs/{cost.Id}", new { cost.Id });
    }

    public static async Task<IResult> UpdateAsync(
        Guid id,
        BillingCostInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["cost"] = [error] });
        }

        var cost = await database.Costs
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (cost is null)
        {
            return Results.NotFound();
        }

        Apply(input, cost);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    public static async Task<IResult> RemoveAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var cost = await database.Costs
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (cost is null)
        {
            return Results.NoContent();
        }

        database.Costs.Remove(cost);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static void Apply(BillingCostInput input, BillingCost cost)
    {
        cost.Date = input.Date;
        cost.Kind = string.IsNullOrWhiteSpace(input.Kind) ? null : input.Kind.Trim();
        cost.Label = input.Label.Trim();
        cost.AmountExclVatCents = input.AmountExclVatCents;
        cost.ContactId = input.ContactId;
        cost.ExternalReference = string.IsNullOrWhiteSpace(input.ExternalReference)
            ? null
            : input.ExternalReference.Trim();
        cost.InvoiceId = input.InvoiceId;
        cost.IsPaid = input.IsPaid;
        cost.PaidOn = input.IsPaid ? input.PaidOn ?? input.Date : null;
    }
}
