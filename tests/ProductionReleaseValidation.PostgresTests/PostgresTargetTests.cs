namespace ProductionReleaseValidation.PostgresTests;

public sealed class PostgresTargetTests
{
    private const string Safe = "Host=127.0.0.1;Username=prv_test_runner;Database=prv_test_0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(null, "true")]
    [InlineData("", "true")]
    [InlineData(Safe, null)]
    [InlineData(Safe, "false")]
    [InlineData("not a connection string", "true")]
    [InlineData("Host=127.0.0.1;Username=prv_test_runner;Database=prv_v1_verify", "true")]
    [InlineData("Host=example.neon.tech;Username=prv_test_runner;Database=prv_test_0123456789abcdef0123456789abcdef", "true")]
    [InlineData("Host=127.0.0.1,example.neon.tech;Username=prv_test_runner;Database=prv_test_0123456789abcdef0123456789abcdef", "true")]
    [InlineData("Host=127.0.0.1;Username=postgres;Database=prv_test_0123456789abcdef0123456789abcdef", "true")]
    [InlineData(Safe + ";Search Path=public", "true")]
    [InlineData(Safe + ";Options=-c search_path=public", "true")]
    [InlineData(Safe + ";Multiplexing=true", "true")]
    public void UnsafeOrMissingConfiguration_IsRejectedWithoutConnecting(string? connection, string? consent) =>
        Assert.Throws<InvalidOperationException>(() => PostgresTarget.Validate(connection, consent));

    [Fact]
    public void ExplicitDisposableTarget_IsAccepted() => Assert.NotEmpty(PostgresTarget.Validate(Safe, "true"));

    [Fact]
    public void NonemptyDatabase_IsRejectedBeforeMigrations() =>
        Assert.Throws<InvalidOperationException>(() => PostgresTarget.RequireEmptyDatabase(true));
}
