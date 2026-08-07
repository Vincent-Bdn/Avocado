using Avocado.Server.Data;
using Avocado.Server.Features.Contacts.Enums;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Contacts.Endpoints;

/// <param name="AttachedToContactId">Null detaches: the person stays, the link goes.</param>
/// <param name="AttachedAs">« Gérant et associé majoritaire », « DAF ». Free text, like every role.</param>
public sealed record AttachmentInput(Guid? AttachedToContactId, string? AttachedAs);

/// <summary>
/// Rattacher une personne à une personne morale, or detach one.
///
/// <para>Its own endpoint rather than a field on <see cref="UpdateContact"/>, for the same reason the
/// favourite star has one: attaching an existing gérant should not be a read-modify-write of their
/// whole fiche, where a stale form in another window could put back an address they corrected an hour
/// ago.</para>
/// </summary>
public static class AttachContact
{
    public static async Task<IResult> HandleAsync(
        Guid id,
        AttachmentInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var contact = await database.Contacts
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (contact is null)
        {
            return Results.NotFound();
        }

        if (input.AttachedToContactId is { } parentId)
        {
            if (parentId == id)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["attachedTo"] = ["Un tiers ne peut pas être rattaché à lui-même."],
                });
            }

            var parent = await database.Contacts
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == parentId, cancellationToken);

            if (parent is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["attachedTo"] = ["Ce tiers n'existe pas."],
                });
            }

            // Only ever a person inside an organisation. Two sociétés related to each other is a
            // different idea — a groupe — and pretending this models it would be a lie the fiche
            // would then have to render.
            if (parent.Type != ContactType.Organisation || contact.Type != ContactType.Individual)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["attachedTo"] =
                        ["Un rattachement va d'une personne physique vers une personne morale."],
                });
            }
        }

        contact.AttachedToContactId = input.AttachedToContactId;
        contact.AttachedAs = input.AttachedToContactId is null || string.IsNullOrWhiteSpace(input.AttachedAs)
            ? null
            : input.AttachedAs.Trim();
        contact.UpdatedAt = DateTimeOffset.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
