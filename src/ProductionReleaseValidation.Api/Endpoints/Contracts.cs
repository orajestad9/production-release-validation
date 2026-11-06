using System.ComponentModel.DataAnnotations;
using ProductionReleaseValidation.Api.Domain;
using DomainValidationResult = ProductionReleaseValidation.Api.Domain.ValidationResult;

namespace ProductionReleaseValidation.Api.Endpoints;

public sealed record RegisterServiceRequest(
    [property: Required, StringLength(100)] string? Name);

public sealed record CreateReleaseRequest(
    [property: Required] Guid? ServiceId,
    [property: Required, StringLength(50)] string? Version,
    [property: Required, StringLength(64)] string? CommitSha);

public sealed record SubmitValidationRequest(
    [property: Required, EnumDataType(typeof(ValidationType))] ValidationType? Type,
    [property: Required, EnumDataType(typeof(ValidationOutcome))] ValidationOutcome? Outcome,
    [property: StringLength(500)] string? Summary = null,
    [property: StringLength(2048)] string? EvidenceUrl = null);

public sealed record ServiceResponse(Guid Id, string Name, DateTimeOffset CreatedAtUtc);

public sealed record ValidationResponse(
    Guid Id,
    Guid ReleaseId,
    ValidationType Type,
    ValidationOutcome Outcome,
    DateTimeOffset RecordedAtUtc,
    string? Summary,
    string? EvidenceUrl)
{
    public static ValidationResponse From(DomainValidationResult result) => new(
        result.Id, result.ReleaseId, result.Type, result.Outcome,
        result.RecordedAtUtc, result.Summary, result.EvidenceUrl);
}

public sealed record ReleaseResponse(
    Guid Id,
    Guid ServiceId,
    string Version,
    string CommitSha,
    DateTimeOffset CreatedAtUtc,
    ReleaseStatus Status,
    IReadOnlyList<ValidationResponse> ValidationResults)
{
    public static ReleaseResponse From(Release release) => new(
        release.Id, release.ServiceId, release.Version, release.CommitSha,
        release.CreatedAtUtc, release.Status,
        release.ValidationResults
            .OrderBy(result => result.RecordedAtUtc)
            .ThenBy(result => result.Id)
            .Select(ValidationResponse.From)
            .ToArray());
}
