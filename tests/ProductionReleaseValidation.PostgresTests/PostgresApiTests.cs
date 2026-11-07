using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ProductionReleaseValidation.PostgresTests;

public sealed class PostgresApiTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task MigrationsAndApiData_SurviveRecreatedHost()
    {
        Assert.Equal(["20261106182901_InitialPostgres"], fixture.AppliedMigrations);
        Guid releaseId;
        string original;
        using (var factory = fixture.CreateFactory())
        using (var client = factory.CreateClient())
        {
            var service = await CreateService(client);
            var release = await CreateRelease(client, service.GetProperty("id").GetGuid());
            releaseId = release.GetProperty("id").GetGuid();
            Assert.Equal("Pending", release.GetProperty("status").GetString());
            Assert.Empty(release.GetProperty("validationResults").EnumerateArray());
            await Submit(client, releaseId, "AutomatedTests", "Passed");
            await AssertStatus(client, releaseId, "Pending", 1);
            await Submit(client, releaseId, "Performance", "Passed");
            await AssertStatus(client, releaseId, "ReadyForDeployment", 2);
            await Submit(client, releaseId, "DeploymentHealth", "Passed");
            await AssertStatus(client, releaseId, "Verified", 3);
            original = (await GetRelease(client, releaseId)).ToString();

            await using var db = fixture.CreateDbContext();
            var storedService = await db.Services.AsNoTracking().SingleAsync(s => s.Id == service.GetProperty("id").GetGuid());
            Assert.Equal(service.GetProperty("name").GetString(), storedService.Name);
            Assert.Equal(service.GetProperty("createdAtUtc").GetDateTimeOffset(), storedService.CreatedAtUtc);
            Assert.Equal(release.GetProperty("createdAtUtc").GetDateTimeOffset(),
                (await GetRelease(client, releaseId)).GetProperty("createdAtUtc").GetDateTimeOffset());
        }

        // Both the host/service provider and its connection instances are new.
        using var restarted = fixture.CreateFactory();
        using var restartedClient = restarted.CreateClient();
        Assert.Equal(original, (await GetRelease(restartedClient, releaseId)).ToString());
        await using var migrated = fixture.CreateDbContext();
        await migrated.Database.MigrateAsync();
        Assert.Equal(original, (await GetRelease(restartedClient, releaseId)).ToString());
    }

    [Fact]
    public async Task DuplicateAndConflictingRequests_PreserveFirstEvidenceAndTimestamps()
    {
        Guid releaseId;
        JsonElement original;
        using (var factory = fixture.CreateFactory())
        using (var client = factory.CreateClient())
        {
            var service = await CreateService(client);
            var serviceId = service.GetProperty("id").GetGuid();
            await AssertProblem(await client.PostAsJsonAsync("/services", new { name = service.GetProperty("name").GetString() }), HttpStatusCode.Conflict);
            var release = await CreateRelease(client, serviceId);
            releaseId = release.GetProperty("id").GetGuid();
            await AssertProblem(await client.PostAsJsonAsync("/releases", new { serviceId, version = "1.0", commitSha = "other" }), HttpStatusCode.Conflict);
            var otherService = await CreateService(client);
            await CreateRelease(client, otherService.GetProperty("id").GetGuid());
            original = await Submit(client, releaseId, "Performance", "Passed");
        }

        using var restarted = fixture.CreateFactory();
        using var restartedClient = restarted.CreateClient();
        var duplicate = await restartedClient.PostAsJsonAsync($"/releases/{releaseId}/validations", new
        {
            type = "Performance", outcome = "Passed", summary = "replacement", evidenceUrl = "https://example.test/replacement"
        });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(original.ToString(), (await ReadJson(duplicate)).ToString());
        await AssertProblem(await restartedClient.PostAsJsonAsync($"/releases/{releaseId}/validations", new { type = "Performance", outcome = "Failed" }), HttpStatusCode.Conflict);
        var history = (await GetRelease(restartedClient, releaseId)).GetProperty("validationResults");
        Assert.Equal(original.ToString(), Assert.Single(history.EnumerateArray()).ToString());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ValidationResults.CountAsync(v => v.ReleaseId == releaseId));
    }

    [Theory]
    [InlineData("AutomatedTests")]
    [InlineData("Performance")]
    [InlineData("DeploymentHealth")]
    public async Task AnyFailure_RemainsFailedAfterHostRecreation(string failedType)
    {
        Guid releaseId;
        using (var factory = fixture.CreateFactory())
        using (var client = factory.CreateClient())
        {
            var service = await CreateService(client);
            releaseId = (await CreateRelease(client, service.GetProperty("id").GetGuid())).GetProperty("id").GetGuid();
            foreach (var type in new[] { "AutomatedTests", "Performance", "DeploymentHealth" })
                await Submit(client, releaseId, type, type == failedType ? "Failed" : "Passed");
            await AssertStatus(client, releaseId, "Failed", 3);
        }
        using var restarted = fixture.CreateFactory();
        using var restartedClient = restarted.CreateClient();
        await AssertStatus(restartedClient, releaseId, "Failed", 3);
    }

    [Fact]
    public async Task MigratedDatabase_EnforcesAllUniqueIndexes()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var service = await CreateService(client);
        var serviceId = service.GetProperty("id").GetGuid();
        var name = service.GetProperty("name").GetString()!;
        var releaseId = (await CreateRelease(client, serviceId)).GetProperty("id").GetGuid();
        await Submit(client, releaseId, "AutomatedTests", "Passed");
        await using var db = fixture.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await AssertConstraint(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Services" ("Id", "Name", "CreatedAtUtc") VALUES ({Guid.NewGuid()}, {name}, {now})
            """), PostgresErrorCodes.UniqueViolation, "IX_Services_Name");
        await AssertConstraint(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Releases" ("Id", "ServiceId", "Version", "CommitSha", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {serviceId}, {"1.0"}, {"abc123"}, {now})
            """), PostgresErrorCodes.UniqueViolation, "IX_Releases_ServiceId_Version");
        await AssertConstraint(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ValidationResults" ("Id", "ReleaseId", "Type", "Outcome", "RecordedAtUtc")
            SELECT {Guid.NewGuid()}, "ReleaseId", "Type", "Outcome", {now} FROM "ValidationResults" WHERE "ReleaseId" = {releaseId}
            """), PostgresErrorCodes.UniqueViolation, "IX_ValidationResults_ReleaseId_Type");
    }

    [Theory]
    [InlineData("Passed", HttpStatusCode.OK)]
    [InlineData("Failed", HttpStatusCode.Conflict)]
    public async Task ConcurrentValidationSubmissions_ResolveUniqueViolation_AndKeepWinner(
        string secondOutcome, HttpStatusCode loserStatus)
    {
        Guid releaseId;
        using (var seedFactory = fixture.CreateFactory())
        using (var seedClient = seedFactory.CreateClient())
        {
            var serviceId = (await CreateService(seedClient)).GetProperty("id").GetGuid();
            releaseId = (await CreateRelease(seedClient, serviceId)).GetProperty("id").GetGuid();
        }
        using var factory = fixture.CreateFactory(new ConcurrentInsertBarrier());
        using var client = factory.CreateClient();
        var path = $"/releases/{releaseId}/validations";
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(path, new { type = "Performance", outcome = "Passed", summary = "first" }),
            client.PostAsJsonAsync(path, new { type = "Performance", outcome = secondOutcome, summary = "second" }));
        var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var loser = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.Created);
        Assert.Equal(loserStatus, loser.StatusCode);
        var original = await ReadJson(winner);
        if (loserStatus == HttpStatusCode.OK)
            Assert.Equal(original.ToString(), (await ReadJson(loser)).ToString());
        else
            await AssertProblem(loser, HttpStatusCode.Conflict);
        var release = await GetRelease(client, releaseId);
        Assert.Equal(original.ToString(), Assert.Single(release.GetProperty("validationResults").EnumerateArray()).ToString());
        Assert.Equal(original.GetProperty("outcome").GetString() == "Failed" ? "Failed" : "Pending",
            release.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MigratedDatabase_EnforcesBothForeignKeys_AndApiReturnsNotFound()
    {
        await using var db = fixture.CreateDbContext();
        var missing = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await AssertConstraint(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Releases" ("Id", "ServiceId", "Version", "CommitSha", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {missing}, {"1.0"}, {"abc123"}, {now})
            """), PostgresErrorCodes.ForeignKeyViolation, "FK_Releases_Services_ServiceId");
        await AssertConstraint(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ValidationResults" ("Id", "ReleaseId", "Type", "Outcome", "RecordedAtUtc")
            VALUES ({Guid.NewGuid()}, {missing}, {0}, {0}, {now})
            """), PostgresErrorCodes.ForeignKeyViolation, "FK_ValidationResults_Releases_ReleaseId");
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        await AssertProblem(await client.PostAsJsonAsync("/releases", new { serviceId = missing, version = "1.0", commitSha = "abc123" }), HttpStatusCode.NotFound);
        await AssertProblem(await client.GetAsync($"/releases/{missing}"), HttpStatusCode.NotFound);
        await AssertProblem(await client.PostAsJsonAsync($"/releases/{missing}/validations", new { type = "Performance", outcome = "Passed" }), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TimestampRoundTrip_DemonstratesPostgresMicrosecondPrecision()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var service = new Api.Domain.Service($"precision-{Guid.NewGuid():N}", instant);
        await using (var writer = fixture.CreateDbContext())
        {
            writer.Services.Add(service);
            await writer.SaveChangesAsync();
            Assert.Equal(instant, service.CreatedAtUtc);
        }
        await using var reader = fixture.CreateDbContext();
        var stored = await reader.Services.AsNoTracking().SingleAsync(s => s.Id == service.Id);
        Assert.Equal(instant.AddTicks(-7), stored.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, stored.CreatedAtUtc.Offset);
    }

    internal static async Task<JsonElement> CreateService(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/services", new { name = $"service-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadJson(response);
        Assert.Equal(0, result.GetProperty("createdAtUtc").GetDateTimeOffset().Ticks % TimeSpan.TicksPerMicrosecond);
        return result;
    }

    internal static async Task<JsonElement> CreateRelease(HttpClient client, Guid serviceId)
    {
        var response = await client.PostAsJsonAsync("/releases", new { serviceId, version = "1.0", commitSha = "abc123" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadJson(response);
        Assert.Equal(0, result.GetProperty("createdAtUtc").GetDateTimeOffset().Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(serviceId, result.GetProperty("serviceId").GetGuid());
        Assert.Equal("1.0", result.GetProperty("version").GetString());
        Assert.Equal("abc123", result.GetProperty("commitSha").GetString());
        Assert.Equal($"/releases/{result.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        return result;
    }

    private static async Task<JsonElement> Submit(HttpClient client, Guid releaseId, string type, string outcome)
    {
        var response = await client.PostAsJsonAsync($"/releases/{releaseId}/validations", new
        {
            type, outcome, summary = "original evidence", evidenceUrl = "https://example.test/evidence"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadJson(response);
        Assert.Equal(0, result.GetProperty("recordedAtUtc").GetDateTimeOffset().Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(releaseId, result.GetProperty("releaseId").GetGuid());
        Assert.Equal(type, result.GetProperty("type").GetString());
        Assert.Equal(outcome, result.GetProperty("outcome").GetString());
        return result;
    }

    internal static async Task<JsonElement> GetRelease(HttpClient client, Guid releaseId)
    {
        var response = await client.GetAsync($"/releases/{releaseId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJson(response);
    }

    internal static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task AssertStatus(HttpClient client, Guid releaseId, string status, int count)
    {
        var release = await GetRelease(client, releaseId);
        Assert.Equal(status, release.GetProperty("status").GetString());
        Assert.Equal(count, release.GetProperty("validationResults").GetArrayLength());
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)expected, (await ReadJson(response)).GetProperty("status").GetInt32());
    }

    private static async Task AssertConstraint(Func<Task<int>> insert, string sqlState, string constraint)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(insert);
        Assert.Equal(sqlState, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
    }
}
