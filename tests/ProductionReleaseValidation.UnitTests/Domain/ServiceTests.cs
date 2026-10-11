using ProductionReleaseValidation.Api.Domain;

namespace ProductionReleaseValidation.UnitTests.Domain;

public class ServiceTests
{
    [Fact]
    public void Constructor_WhenNameIsNull_ThrowsArgumentException()
    {
        // Arrange
        string name = null!;
        var createdAtUtc = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Service(name, createdAtUtc));
    }

    [Fact]
    public void Constructor_WhenNameIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        string name = "";
        var createdAtUtc = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Service(name, createdAtUtc));
    }

    [Fact]
    public void Constructor_WhenNameIsWhitespace_ThrowsArgumentException()
    {
        // Arrange
        string name = "   ";
        var createdAtUtc = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Service(name, createdAtUtc));
    }

    [Fact]
    public void Constructor_WhenValidParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        string name = "MyService";
        var createdAtUtc = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        // Act
        var service = new Service(name, createdAtUtc);

        // Assert
        Assert.Equal(name, service.Name);
        Assert.Equal(createdAtUtc, service.CreatedAtUtc);
    }
}