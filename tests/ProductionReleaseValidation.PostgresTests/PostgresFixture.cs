using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProductionReleaseValidation.Api.Infrastructure.Persistence;

namespace ProductionReleaseValidation.PostgresTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private string _connectionString = null!;
    public string[] AppliedMigrations { get; private set; } = [];

    public async Task InitializeAsync()
    {
        _connectionString = PostgresTarget.Validate(
            Environment.GetEnvironmentVariable(PostgresTarget.ConnectionVariable),
            Environment.GetEnvironmentVariable(PostgresTarget.ConsentVariable));

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
                  AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%'
                  AND c.relkind IN ('r', 'p', 'v', 'm', 'S', 'f'))
            """, connection);
        PostgresTarget.RequireEmptyDatabase((bool)(await command.ExecuteScalarAsync())!);

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        AppliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        // Reapplying migrations must preserve the schema and data.
        await db.Database.MigrateAsync();
    }

    public ProductionReleaseValidationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ProductionReleaseValidationDbContext>()
            .UseNpgsql(_connectionString).Options);

    public PostgresApiFactory CreateFactory(SaveChangesInterceptor? interceptor = null) => new(_connectionString, interceptor);

    // The fixture never drops, truncates, resets or deletes a database. Its caller owns disposal.
    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class PostgresApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly SaveChangesInterceptor? _interceptor;

    internal PostgresApiFactory(string connectionString, SaveChangesInterceptor? interceptor)
    {
        _connectionString = connectionString;
        _interceptor = interceptor;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
        if (_interceptor is not null)
            builder.ConfigureServices(services => services.ConfigureDbContext<ProductionReleaseValidationDbContext>(
                options => options.AddInterceptors(_interceptor)));
    }
}
