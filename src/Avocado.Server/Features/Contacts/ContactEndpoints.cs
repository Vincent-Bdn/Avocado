using Avocado.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Contacts;

/// <summary>
/// The slice pattern for everything that follows: request shape, validation, handler and route in one
/// file, reachable through DI without a mediator in between.
/// </summary>
public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContacts(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/contacts");

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return routes;
    }

    public sealed record ContactSummary(Guid Id, ContactType Type, string DisplayName, string? Email, string? Phone);

    public sealed record ContactInput(
        ContactType Type,
        string? Civility,
        string? LastName,
        string? FirstName,
        DateOnly? DateOfBirth,
        string? LegalName,
        string? Siren,
        string? LegalForm,
        string? Email,
        string? Phone,
        string? Address,
        string? Notes)
    {
        public string? Validate() => Type switch
        {
            ContactType.Individual when string.IsNullOrWhiteSpace(LastName) =>
                "Le nom est obligatoire pour une personne physique.",
            ContactType.Organisation when string.IsNullOrWhiteSpace(LegalName) =>
                "La raison sociale est obligatoire pour une personne morale.",
            // Nine digits; the annuaire lookup returns it grouped, so normalise before comparing.
            ContactType.Organisation when Siren is not null && Digits(Siren).Length is not (0 or 9) =>
                "Un SIREN comporte 9 chiffres.",
            _ => null,
        };

        private static string Digits(string value) => new([.. value.Where(char.IsDigit)]);
    }

    private static async Task<IResult> ListAsync(AvocadoDbContext database, string? search, CancellationToken ct)
    {
        var query = database.Contacts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c =>
                EF.Functions.Like(c.LastName ?? string.Empty, pattern) ||
                EF.Functions.Like(c.FirstName ?? string.Empty, pattern) ||
                EF.Functions.Like(c.LegalName ?? string.Empty, pattern) ||
                EF.Functions.Like(c.Email ?? string.Empty, pattern));
        }

        var contacts = await query
            .OrderBy(c => c.LastName ?? c.LegalName)
            .Take(200)
            .ToListAsync(ct);

        return Results.Ok(contacts.Select(Summarise));
    }

    private static async Task<IResult> GetAsync(Guid id, AvocadoDbContext database, CancellationToken ct)
    {
        var contact = await database.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return contact is null ? Results.NotFound() : Results.Ok(contact);
    }

    private static async Task<IResult> CreateAsync(ContactInput input, AvocadoDbContext database, CancellationToken ct)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["contact"] = [error] });
        }

        var contact = new Contact();
        Apply(input, contact);

        database.Contacts.Add(contact);
        await database.SaveChangesAsync(ct);

        return Results.Created($"/api/contacts/{contact.Id}", contact);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, ContactInput input, AvocadoDbContext database, CancellationToken ct)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["contact"] = [error] });
        }

        var contact = await database.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return Results.NotFound();
        }

        Apply(input, contact);
        contact.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(ct);

        return Results.Ok(contact);
    }

    private static async Task<IResult> DeleteAsync(Guid id, AvocadoDbContext database, CancellationToken ct)
    {
        var contact = await database.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return Results.NotFound();
        }

        // MatterParty restricts this delete at the database level, but a foreign-key violation surfaces
        // as an opaque 500. Answer the question the user actually asked instead.
        var matterCount = await database.MatterParties.CountAsync(p => p.ContactId == id, ct);
        if (matterCount > 0)
        {
            return Results.Problem(
                title: "Tiers rattaché à des dossiers",
                detail: $"Ce tiers intervient dans {matterCount} dossier(s). Retirez-le de ces dossiers avant de le supprimer.",
                statusCode: StatusCodes.Status409Conflict);
        }

        database.Contacts.Remove(contact);
        await database.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static ContactSummary Summarise(Contact c) =>
        new(c.Id, c.Type, c.DisplayName, c.Email, c.Phone);

    private static void Apply(ContactInput input, Contact contact)
    {
        contact.Type = input.Type;
        contact.Civility = input.Civility;
        contact.LastName = input.LastName;
        contact.FirstName = input.FirstName;
        contact.DateOfBirth = input.DateOfBirth;
        contact.LegalName = input.LegalName;
        contact.Siren = input.Siren;
        contact.LegalForm = input.LegalForm;
        contact.Email = input.Email;
        contact.Phone = input.Phone;
        contact.Address = input.Address;
        contact.Notes = input.Notes;
    }
}
