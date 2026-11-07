using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductionReleaseValidation.Api.Domain;
using ProductionReleaseValidation.Api.Infrastructure.Persistence;
using AnnotationValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;
using DomainValidationResult = ProductionReleaseValidation.Api.Domain.ValidationResult;

namespace ProductionReleaseValidation.Api.Endpoints;

public static class ReleaseEndpoints
{
    public static void MapReleaseEndpoints(this WebApplication app)
    {
        app.MapPost("/services", RegisterServiceAsync);
        app.MapPost("/releases", CreateReleaseAsync);
        app.MapPost("/releases/{id}/validations", SubmitValidationAsync);
        app.MapGet("/releases/{id}", GetReleaseAsync);
    }

    private static async Task<IResult> RegisterServiceAsync(
        RegisterServiceRequest request,
        ProductionReleaseValidationDbContext db,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } error)
        {
            return error;
        }

        if (await db.Services.AnyAsync(service => service.Name == request.Name, cancellationToken))
        {
            return Conflict("A service with this name already exists.");
        }

        var service = new Service(request.Name!, PersistenceTimestamp());
        db.Services.Add(service);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("A service with this name already exists.");
        }

        return Results.Created($"/services/{service.Id}",
            new ServiceResponse(service.Id, service.Name, service.CreatedAtUtc));
    }

    private static async Task<IResult> CreateReleaseAsync(
        CreateReleaseRequest request,
        ProductionReleaseValidationDbContext db,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } error)
        {
            return error;
        }

        if (request.ServiceId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ServiceId)] = ["Service ID cannot be empty."]
            });
        }

        if (!await db.Services.AnyAsync(service => service.Id == request.ServiceId, cancellationToken))
        {
            return NotFound("Service");
        }

        if (await db.Releases.AnyAsync(release => release.ServiceId == request.ServiceId &&
                release.Version == request.Version, cancellationToken))
        {
            return Conflict("This version already exists for the service.");
        }

        var release = new Release(request.ServiceId!.Value, request.Version!, request.CommitSha!,
            PersistenceTimestamp());
        db.Releases.Add(release);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("This version already exists for the service.");
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            // The service may have been removed after the existence check.
            return NotFound("Service");
        }

        return Results.Created($"/releases/{release.Id}", ReleaseResponse.From(release));
    }

    private static async Task<IResult> SubmitValidationAsync(
        Guid id,
        SubmitValidationRequest request,
        ProductionReleaseValidationDbContext db,
        CancellationToken cancellationToken)
    {
        if (Validate(request) is { } error)
        {
            return error;
        }

        var release = await db.Releases.Include(release => release.ValidationResults)
            .SingleOrDefaultAsync(release => release.Id == id, cancellationToken);
        if (release is null)
        {
            return NotFound("Release");
        }

        var existing = release.ValidationResults.SingleOrDefault(result => result.Type == request.Type);
        if (existing is not null)
        {
            return ExistingValidation(existing, request.Outcome!.Value);
        }

        var result = new DomainValidationResult(request.Type!.Value, request.Outcome!.Value,
            PersistenceTimestamp(), request.Summary, request.EvidenceUrl);
        release.AddValidationResult(result);
        // Explicitly insert the new child: its domain-generated key is already populated.
        db.ValidationResults.Add(result);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent request may have recorded this type after our read.
            // Discard the failed insert and compare against the first persisted evidence.
            db.ChangeTracker.Clear();
            existing = await db.ValidationResults.AsNoTracking()
                .SingleOrDefaultAsync(value => value.ReleaseId == id && value.Type == request.Type,
                    cancellationToken);
            if (existing is null)
            {
                return Conflict("Validation evidence could not be recorded because of a conflicting submission.");
            }

            return ExistingValidation(existing, request.Outcome.Value);
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            return NotFound("Release");
        }

        return Results.Created($"/releases/{id}", ValidationResponse.From(result));
    }

    private static async Task<IResult> GetReleaseAsync(
        Guid id,
        ProductionReleaseValidationDbContext db,
        CancellationToken cancellationToken)
    {
        var release = await db.Releases.AsNoTracking()
            .Include(release => release.ValidationResults)
            .SingleOrDefaultAsync(release => release.Id == id, cancellationToken);

        return release is null ? NotFound("Release") : Results.Ok(ReleaseResponse.From(release));
    }

    private static IResult? Validate(object request)
    {
        var results = new List<AnnotationValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, true))
        {
            return null;
        }

        return Results.ValidationProblem(results
            .SelectMany(result => result.MemberNames.Select(member => new { member, result.ErrorMessage }))
            .GroupBy(result => result.member)
            .ToDictionary(group => group.Key,
                group => group.Select(result => result.ErrorMessage!).ToArray()));
    }

    private static IResult ExistingValidation(DomainValidationResult existing, ValidationOutcome outcome) =>
        existing.Outcome == outcome
            ? Results.Ok(ValidationResponse.From(existing))
            : Conflict("A different outcome is already recorded for this validation type.");

    private static IResult NotFound(string resource) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound,
            title: "Resource not found", detail: $"{resource} was not found.");

    private static IResult Conflict(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Conflict", detail: detail);

    // PostgreSQL stores microseconds; return the same timestamp before and after a reload.
    private static DateTimeOffset PersistenceTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}
