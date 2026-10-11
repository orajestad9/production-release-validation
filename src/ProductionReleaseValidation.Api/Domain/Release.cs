namespace ProductionReleaseValidation.Api.Domain
{
    public class Release
    {
        public Guid Id { get; private set; } = Guid.CreateVersion7();
        private readonly List<ValidationResult> _validationResults = [];
        public string Version { get; }
        public string CommitSha { get; }

        public Release(string version, string commitSha)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
            ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

            Version = version;
            CommitSha = commitSha;
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
                _validationResults.Add(result);
            }
            else if (existingResult.Outcome == result.Outcome)
            {
                // No change in outcome, do nothing
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cannot add a validation result of type {result.Type} with outcome {result.Outcome} because a result of the same type with a different outcome already exists.");
            }
        }
    }
}
