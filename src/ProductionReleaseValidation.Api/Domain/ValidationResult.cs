namespace ProductionReleaseValidation.Api.Domain
{
    public class ValidationResult(
    ValidationType type,
    ValidationOutcome outcome,
    DateTimeOffset recordedAtUtc,
    string? summary = null,
    string? evidenceUrl = null)
    {
        public Guid Id { get; private set; } = Guid.CreateVersion7();
        public ValidationType Type { get; } = type;
        public ValidationOutcome Outcome { get; } = outcome;
        public DateTimeOffset RecordedAtUtc { get; } = recordedAtUtc;
        public string? Summary { get; } = summary;
        public string? EvidenceUrl { get; } = evidenceUrl;
    }
}
