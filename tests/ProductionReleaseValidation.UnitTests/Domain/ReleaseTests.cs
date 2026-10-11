using ProductionReleaseValidation.Api.Domain;

namespace ProductionReleaseValidation.UnitTests.Domain;

public class ReleaseTests
{
    [Fact]
    public void Status_WhenNoValidationResultsExist_IsPending()
    {
        // Arrange
        var release = new Release("1.0.0", "abc123");

        // Act
        var status = release.Status;

        // Assert
        Assert.Equal(ReleaseStatus.Pending, status);
    }

    [Fact]
    public void Status_WhenPreDeploymentValidationsPass_IsReadyForDeployment()
    {
        // Arrange
        var release = new Release("1.0.0", "abc123");
        var timestamp = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.AutomatedTests,
                ValidationOutcome.Passed,
                timestamp
            ));

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.Performance,
                ValidationOutcome.Passed,
                timestamp
            ));

        // Act
        var status = release.Status;

        // Assert
        Assert.Equal(ReleaseStatus.ReadyForDeployment, status);
    }

    [Fact]
    public void Status_WhenAllValidationsPass_IsVerified()
    {
        // Arrange
        var release = new Release("1.0.0", "abc123");
        var timestamp = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.AutomatedTests,
                ValidationOutcome.Passed,
                timestamp
            ));

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.Performance,
                ValidationOutcome.Passed,
                timestamp
            ));

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.DeploymentHealth,
                ValidationOutcome.Passed,
                timestamp
            ));

        // Act
        var status = release.Status;

        // Assert
        Assert.Equal(ReleaseStatus.Verified, status);
    }

    [Fact]
    public void Status_WhenAnyValidationFails_IsFailed()
    {
        // Arrange
        var release = new Release("1.0.0", "abc123");
        var timestamp = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.AutomatedTests,
                ValidationOutcome.Passed,
                timestamp
            ));

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.Performance,
                ValidationOutcome.Failed,
                timestamp
            ));

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.DeploymentHealth,
                ValidationOutcome.Passed,
                timestamp
            ));

        // Act
        var status = release.Status;

        // Assert
        Assert.Equal(ReleaseStatus.Failed, status);
    }

    [Fact]
    public void Status_WhenPreDeploymentValidationIsIncomplete_IsPending()
    {
        // Arrange
        var release = new Release("1.0.0", "abc123");
        var timestamp = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.AutomatedTests,
                ValidationOutcome.Passed,
                timestamp
            ));

        // Act
        var status = release.Status;

        // Assert
        Assert.Equal(ReleaseStatus.Pending, status);
    }

    [Fact]
    public void AddValidationResult_WhenSameOutcomeAlreadyExists_DoesNotAddDuplicate()
    {
        // Arrange
        var release = new Release("1.0.0", "abc123");
        var timestamp = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        var firstResult = new ValidationResult(
            ValidationType.Performance,
            ValidationOutcome.Passed,
            timestamp);

        var duplicateResult = new ValidationResult(
            ValidationType.Performance,
            ValidationOutcome.Passed,
            timestamp.AddMinutes(1));

        release.AddValidationResult(firstResult);
        release.AddValidationResult(duplicateResult);

        // Assert
        var storedResult = Assert.Single(release.ValidationResults);

        Assert.Same(firstResult, storedResult);
    }

    [Fact]
    public void AddValidationResult_WhenConflictingOutcomeExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var release = new Release("1.0.0", "abc123");
        var timestamp = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        release.AddValidationResult(
            new ValidationResult(
                ValidationType.Performance,
                ValidationOutcome.Failed,
                timestamp
            ));

        // Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            release.AddValidationResult(
                    new ValidationResult(
                        ValidationType.Performance,
                        ValidationOutcome.Passed,
                        timestamp.AddMinutes(1)
                    ));
        });

        Assert.Single(release.ValidationResults);
    }

    [Fact]
    public void Constructor_WhenVersionIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        string version = "";
        string commitSha = "abc123";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Release(version, commitSha));
    }

    [Fact]
    public void Constructor_WhenCommitShaIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        string version = "1.0.0";
        string commitSha = "";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Release(version, commitSha));
    }

    [Fact]
    public void Constructor_WhenVersionIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        string version = "   ";
        string commitSha = "abc123";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Release(version, commitSha));
    }

    [Fact]
    public void Constructor_WhenCommitShaIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        string version = "1.0.0";
        string commitSha = "   ";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Release(version, commitSha));
    }

    [Fact]
    public void Release_Constructor_WhenCreated_AssignsNonEmptyId()
    {
        // Arrange
        string version = "1.0.0";
        string commitSha = "abc123";

        // Act
        var release = new Release(version, commitSha);

        // Assert
        Assert.NotEqual(Guid.Empty, release.Id);
    }

    [Fact]
    public void ValidationResult_Constructor_WhenCreated_AssignsNonEmptyId()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        // Act
        var result = new ValidationResult(
            ValidationType.AutomatedTests,
            ValidationOutcome.Passed,
            timestamp);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public void Constructor_WhenCreatingMultipleReleases_AssignsUniqueIds()
    {
        // Arrange
        string version1 = "1.0.0";
        string commitSha1 = "abc123";

        string version2 = "1.0.1";
        string commitSha2 = "def456";

        // Act
        var release1 = new Release(version1, commitSha1);
        var release2 = new Release(version2, commitSha2);

        // Assert
        Assert.NotEqual(release1.Id, release2.Id);
    }
}