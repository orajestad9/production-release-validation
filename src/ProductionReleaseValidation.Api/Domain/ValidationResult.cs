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
        public Guid ReleaseId { get; private set; }
        public ValidationType Type { get; } = type;
        public ValidationOutcome Outcome { get; } = outcome;
        public DateTimeOffset RecordedAtUtc { get; } = recordedAtUtc;
        public string? Summary { get; } = summary;
        public string? EvidenceUrl { get; } = evidenceUrl;

        internal void AssignToRelease(Guid releaseId)
        {
            if (releaseId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Release ID cannot be empty.",
                    nameof(releaseId));
            }

            if (ReleaseId == Guid.Empty)
            {
                ReleaseId = releaseId;
                return;
            }

            if (ReleaseId == releaseId)
            {
                return;
            }

            throw new InvalidOperationException(
                "Validation result is already assigned to a different release.");
        }
    }
}
