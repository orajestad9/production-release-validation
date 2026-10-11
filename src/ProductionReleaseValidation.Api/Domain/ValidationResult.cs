namespace ProductionReleaseValidation.Api.Domain
{
    public class ValidationResult(
    ValidationType type,
    ValidationOutcome outcome,
    DateTimeOffset recordedAtUtc,
    string? summary = null,
    string? evidenceUrl = null)
    {
        public ValidationType Type { get; set; } = type;
        public ValidationOutcome Outcome { get; set; } = outcome;
        public DateTimeOffset RecordedAtUtc { get; set; } = recordedAtUtc;
        public string? Summary { get; set; } = summary;
        public string? EvidenceUrl { get; set; } = evidenceUrl;
    }
}