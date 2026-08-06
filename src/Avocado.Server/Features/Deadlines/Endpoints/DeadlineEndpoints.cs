using Avocado.Server.Data;
using Avocado.Server.Features.Deadlines.Endpoints.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Avocado.Server.Features.Deadlines.Endpoints;

public static class DeadlineEndpoints
{
    public static IEndpointRouteBuilder MapDeadlines(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/matters/{matterId:guid}/deadlines", ListAsync).WithTags("Deadlines");
        routes.MapGet("/api/deadlines", ListAllAsync).WithTags("Deadlines");
        routes.MapPost("/api/matters/{matterId:guid}/deadlines", CreateAsync).WithTags("Deadlines");

        var group = routes.MapGroup("/api/deadlines").WithTags("Deadlines");
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return routes;
    }

    private static async Task<IResult> ListAsync(
        Guid matterId,
        AvocadoDbContext database,
        TimeProvider clock,
        bool includeDone = false,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var query = database.Deadlines
            .AsNoTracking()
            .Where(deadline => deadline.MatterId == matterId);

        if (!includeDone)
        {
            query = query.Where(deadline => !deadline.IsDone);
        }

        var deadlines = await query
            .OrderBy(deadline => deadline.IsDone)
            .ThenBy(deadline => deadline.Date)
            .ThenBy(deadline => deadline.Time)
            .Select(deadline => new DeadlineItem(
                deadline.Id,
                deadline.Date,
                deadline.Time,
                deadline.Type,
                deadline.Label,
                deadline.RemindDaysBefore,
                deadline.IsDone,
                default))
            .ToListAsync(cancellationToken);

        // Urgency is a domain rule, not a SQL expression.
        return Results.Ok(deadlines.Select(deadline => deadline with
        {
            Urgency = DeadlineUrgencyRule.For(deadline.Date, today),
        }));
    }

    /// <summary>
    /// Every open deadline across the practice, for the Échéances section. Closed matters are left
    /// out: closing hides their deadlines rather than deleting them.
    /// </summary>
    private static async Task<IResult> ListAllAsync(
        AvocadoDbContext database,
        TimeProvider clock,
        bool includeDone = false,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        var query = database.Deadlines
            .AsNoTracking()
            .Where(deadline => deadline.Matter!.ClosedOn == null);

        if (!includeDone)
        {
            query = query.Where(deadline => !deadline.IsDone);
        }

        var deadlines = await query
            .OrderBy(deadline => deadline.Date)
            .ThenBy(deadline => deadline.Time)
            .Select(deadline => new MatterDeadlineItem(
                deadline.Id,
                deadline.MatterId,
                deadline.Matter!.Reference,
                deadline.Matter.Name,
                deadline.Date,
                deadline.Time,
                deadline.Type,
                deadline.Label,
                deadline.IsDone,
                default))
            .ToListAsync(cancellationToken);

        return Results.Ok(deadlines.Select(deadline => deadline with
        {
            Urgency = DeadlineUrgencyRule.For(deadline.Date, today),
        }));
    }

    private static async Task<IResult> CreateAsync(
        Guid matterId,
        DeadlineInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["deadline"] = [error] });
        }

        if (!await database.Matters.AnyAsync(matter => matter.Id == matterId, cancellationToken))
        {
            return Results.NotFound();
        }

        var deadline = new Deadline
        {
            MatterId = matterId,
            Date = input.Date,
            Time = input.Time,
            Type = input.Type,
            Label = input.Label.Trim(),
            RemindDaysBefore = input.RemindDaysBefore,
        };

        database.Deadlines.Add(deadline);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/deadlines/{deadline.Id}", new { deadline.Id });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        DeadlineInput input,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        if (input.Validate() is { } error)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["deadline"] = [error] });
        }

        var deadline = await database.Deadlines
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (deadline is null)
        {
            return Results.NotFound();
        }

        deadline.Date = input.Date;
        deadline.Time = input.Time;
        deadline.Type = input.Type;
        deadline.Label = input.Label.Trim();
        deadline.RemindDaysBefore = input.RemindDaysBefore;

        // Marking one done is what removes it from the accueil and the rail's urgency dot; it is
        // never deleted, so the chronology of what was watched for survives.
        deadline.IsDone = input.IsDone;

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        AvocadoDbContext database,
        CancellationToken cancellationToken)
    {
        var deadline = await database.Deadlines
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (deadline is null)
        {
            return Results.NotFound();
        }

        database.Deadlines.Remove(deadline);
        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
