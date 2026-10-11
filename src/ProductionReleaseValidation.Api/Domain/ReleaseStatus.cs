namespace ProductionReleaseValidation.Api.Domain
{
    public enum ReleaseStatus
    {
        Pending,
        ReadyForDeployment,
        Failed,
        Verified
    }
}