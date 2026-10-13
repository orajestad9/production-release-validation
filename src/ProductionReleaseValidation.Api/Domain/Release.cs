namespace ProductionReleaseValidation.Api.Domain
{
    public class Release
    {
        public Guid Id { get; private set; } = Guid.CreateVersion7();
        public Guid ServiceId { get; }
        private readonly List<ValidationResult> _validationResults = [];
        public string Version { get; }
        public string CommitSha { get; }
        public DateTimeOffset CreatedAtUtc { get; }

        public Release(Guid serviceId, string version, string commitSha, DateTimeOffset createdAtUtc)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
            ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

            if (serviceId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Service ID cannot be empty.",
                    nameof(serviceId));
            }

            Version = version;
            CommitSha = commitSha;
            ServiceId = serviceId;
            CreatedAtUtc = createdAtUtc;
        }

        public ReleaseStatus Status
        {
            get
            {
                if (_validationResults.Any(r => r.Outcome == ValidationOutcome.Failed))
                {
                    return ReleaseStatus.Failed;
                }

                bool automatedTestsPassed = _validationResults.Any(r =>
                    r.Type == ValidationType.AutomatedTests &&
                    r.Outcome == ValidationOutcome.Passed);

                bool performancePassed = _validationResults.Any(r =>
                    r.Type == ValidationType.Performance &&
                    r.Outcome == ValidationOutcome.Passed);

                bool deploymentHealthPassed = _validationResults.Any(r =>
                    r.Type == ValidationType.DeploymentHealth &&
                    r.Outcome == ValidationOutcome.Passed);

                if (automatedTestsPassed &&
                    performancePassed &&
                    deploymentHealthPassed)
                {
                    return ReleaseStatus.Verified;
                }

                if (automatedTestsPassed && performancePassed)
                {
                    return ReleaseStatus.ReadyForDeployment;
                }

                return ReleaseStatus.Pending;
            }
        }

        public IReadOnlyCollection<ValidationResult> ValidationResults => _validationResults;

        public void AddValidationResult(ValidationResult result)
        {
            var existingResult = _validationResults.FirstOrDefault(
                r => r.Type == result.Type);

            if (existingResult is null)
            {
                result.AssignToRelease(Id);
                _validationResults.Add(result);
                return;
            }

            if (existingResult.Outcome == result.Outcome)
            {
                // Idempotent duplicate; keep the first accepted result.
                return;
            }

            throw new InvalidOperationException(
                $"Cannot add a validation result of type {result.Type} with outcome {result.Outcome} because a result of the same type with a different outcome already exists.");
        }
    }
}
