using System.Text.RegularExpressions;
using Npgsql;

namespace ProductionReleaseValidation.PostgresTests;

internal static class PostgresTarget
{
    public const string ConnectionVariable = "PRV_TEST_POSTGRES";
    public const string ConsentVariable = "PRV_TEST_POSTGRES_DISPOSABLE";

    // Validate before opening any connection. Never read application settings or user secrets.
    public static string Validate(string? connectionString, string? consent)
    {
        if (consent != "true" || string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Set {ConnectionVariable} and {ConsentVariable}=true for a fresh disposable PostgreSQL target. See TESTING.md.");

        NpgsqlConnectionStringBuilder target;
        try
        {
            target = new(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Invalid PostgreSQL test configuration. See TESTING.md.");
        }

        if (target.Host is not ("127.0.0.1" or "localhost" or "::1") ||
            target.Username != "prv_test_runner" ||
            !Regex.IsMatch(target.Database ?? "", @"\Aprv_test_[0-9a-f]{32}\z") ||
            !string.IsNullOrEmpty(target.Options) ||
            !string.IsNullOrEmpty(target.SearchPath) || target.Multiplexing)
            throw new InvalidOperationException(
                "Unsafe PostgreSQL test target: require loopback, user prv_test_runner, database prv_test_<32 lowercase hex>, and no Options, Search Path or Multiplexing.");

        // Make connection behavior independent of ambient PG* defaults.
        target.Timeout = 5;
        target.CommandTimeout = 30;
        target.Pooling = false;
        target.IncludeErrorDetail = false;
        return target.ConnectionString;
    }

    public static void RequireEmptyDatabase(bool hasObjects)
    {
        if (hasObjects)
            throw new InvalidOperationException("PostgreSQL test database is not empty; refusing to migrate or reset it. Create a fresh disposable target.");
    }
}
