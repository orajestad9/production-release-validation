using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProductionReleaseValidation.Api.Infrastructure.Persistence;

namespace ProductionReleaseValidation.IntegrationTests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\"\"}")]
    [InlineData("{\"name\":\"   \"}")]
    public async Task RegisterService_InvalidName_ReturnsValidationProblem(string json)
    {
        await AssertProblem(await PostJson("/services", json), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisterService_ExcessiveName_ReturnsBadRequest()
    {
        await AssertProblem(await _client.PostAsJsonAsync("/services", new { name = new string('a', 101) }),
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegisterService_ReturnsGeneratedMetadata_AndDuplicateConflicts()
    {
        var name = $"service-{Guid.NewGuid():N}";
        var before = DateTimeOffset.UtcNow;
        var response = await _client.PostAsJsonAsync("/services", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var service = await ReadJson(response);
        Assert.NotEqual(Guid.Empty, service.GetProperty("id").GetGuid());
        Assert.Equal(name, service.GetProperty("name").GetString());
        Assert.InRange(service.GetProperty("createdAtUtc").GetDateTimeOffset(), before, DateTimeOffset.UtcNow);

        await AssertProblem(await _client.PostAsJsonAsync("/services", new { name }), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"serviceId\":\"00000000-0000-0000-0000-000000000000\",\"version\":\"1\",\"commitSha\":\"abc\"}")]
    [InlineData("{\"serviceId\":\"invalid\",\"version\":\"1\",\"commitSha\":\"abc\"}")]
    [InlineData("{\"serviceId\":\"019a0000-0000-7000-8000-000000000001\",\"version\":null,\"commitSha\":\"abc\"}")]
    [InlineData("{\"serviceId\":\"019a0000-0000-7000-8000-000000000001\",\"version\":\" \",\"commitSha\":\"abc\"}")]
    [InlineData("{\"serviceId\":\"019a0000-0000-7000-8000-000000000001\",\"version\":\"1\",\"commitSha\":null}")]
    [InlineData("{\"serviceId\":\"019a0000-0000-7000-8000-000000000001\",\"version\":\"1\",\"commitSha\":\"\"}")]
    public async Task CreateRelease_InvalidRequest_ReturnsBadRequest(string json)
    {
        await AssertProblem(await PostJson("/releases", json), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(51, 3)]
    [InlineData(1, 65)]
    public async Task CreateRelease_ExcessiveFields_ReturnsBadRequest(int versionLength, int shaLength)
    {
        var response = await _client.PostAsJsonAsync("/releases", new
        {
            serviceId = Guid.NewGuid(), version = new string('v', versionLength), commitSha = new string('a', shaLength)
        });
        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateRelease_MissingService_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/releases", new
        {
            serviceId = Guid.NewGuid(), version = "1.0", commitSha = "abc123"
        });
        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateRelease_ReturnsPendingDetails_AndEnforcesVersionPerService()
    {
        var serviceId = await CreateService();
        var request = new { serviceId, version = "release-candidate", commitSha = "short-sha" };
        var before = DateTimeOffset.UtcNow;
        var response = await _client.PostAsJsonAsync("/releases", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var release = await ReadJson(response);
        var releaseId = release.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, releaseId);
        Assert.Equal(serviceId, release.GetProperty("serviceId").GetGuid());
        Assert.Equal(request.version, release.GetProperty("version").GetString());
        Assert.Equal(request.commitSha, release.GetProperty("commitSha").GetString());
        Assert.InRange(release.GetProperty("createdAtUtc").GetDateTimeOffset(), before, DateTimeOffset.UtcNow);
        Assert.Equal("Pending", release.GetProperty("status").GetString());
        Assert.Empty(release.GetProperty("validationResults").EnumerateArray());
        Assert.Equal($"/releases/{releaseId}", response.Headers.Location?.OriginalString);

        var retrieved = await _client.GetAsync($"/releases/{releaseId}");
        Assert.Equal(HttpStatusCode.OK, retrieved.StatusCode);
        Assert.Equal(release.ToString(), (await ReadJson(retrieved)).ToString());
        await AssertProblem(await _client.PostAsJsonAsync("/releases", request), HttpStatusCode.Conflict);

        var otherServiceId = await CreateService();
        var other = await _client.PostAsJsonAsync("/releases", new
        {
            serviceId = otherServiceId, request.version, request.commitSha
        });
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task ReleaseOperations_MissingRelease_ReturnNotFound(string method)
    {
        var path = $"/releases/{Guid.NewGuid()}";
        var response = method == "GET"
            ? await _client.GetAsync(path)
            : await _client.PostAsJsonAsync($"{path}/validations", new { type = "Performance", outcome = "Passed" });
        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"outcome\":\"Passed\"}")]
    [InlineData("{\"type\":\"Performance\"}")]
    [InlineData("{\"type\":null,\"outcome\":\"Passed\"}")]
    [InlineData("{\"type\":\"Performance\",\"outcome\":null}")]
    [InlineData("{\"type\":\"Unknown\",\"outcome\":\"Passed\"}")]
    [InlineData("{\"type\":\"Performance\",\"outcome\":\"Unknown\"}")]
    [InlineData("{\"type\":999,\"outcome\":0}")]
    [InlineData("{\"type\":1,\"outcome\":999}")]
    public async Task SubmitValidation_UnsupportedOrMissingEnums_ReturnsBadRequest(string json)
    {
        await AssertProblem(await PostJson($"/releases/{Guid.NewGuid()}/validations", json), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(501, 10)]
    [InlineData(10, 2049)]
    public async Task SubmitValidation_ExcessiveMetadata_ReturnsBadRequest(int summaryLength, int evidenceLength)
    {
        var response = await _client.PostAsJsonAsync($"/releases/{Guid.NewGuid()}/validations", new
        {
            type = "Performance", outcome = "Passed",
            summary = new string('s', summaryLength), evidenceUrl = new string('e', evidenceLength)
        });
        await AssertProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmitValidation_DuplicateReturnsOriginalEvidence_ConflictKeepsOriginal()
    {
        var releaseId = await CreateRelease();
        var path = $"/releases/{releaseId}/validations";
        var before = DateTimeOffset.UtcNow;
        var response = await _client.PostAsJsonAsync(path, new
        {
            type = "Performance", outcome = "Passed", summary = "accepted", evidenceUrl = "https://example.test/evidence"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var original = await ReadJson(response);
        Assert.NotEqual(Guid.Empty, original.GetProperty("id").GetGuid());
        Assert.Equal(releaseId, original.GetProperty("releaseId").GetGuid());
        Assert.Equal("Performance", original.GetProperty("type").GetString());
        Assert.Equal("Passed", original.GetProperty("outcome").GetString());
        Assert.InRange(original.GetProperty("recordedAtUtc").GetDateTimeOffset(), before, DateTimeOffset.UtcNow);
        Assert.Equal("accepted", original.GetProperty("summary").GetString());
        Assert.Equal("https://example.test/evidence", original.GetProperty("evidenceUrl").GetString());

        var duplicate = await _client.PostAsJsonAsync(path, new
        {
            type = "Performance", outcome = "Passed", summary = "replacement", evidenceUrl = "https://example.test/other"
        });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(original.ToString(), (await ReadJson(duplicate)).ToString());
        await AssertProblem(await _client.PostAsJsonAsync(path, new { type = "Performance", outcome = "Failed" }),
            HttpStatusCode.Conflict);

        var release = await ReadJson(await _client.GetAsync($"/releases/{releaseId}"));
        Assert.Equal(original.ToString(), Assert.Single(release.GetProperty("validationResults").EnumerateArray()).ToString());
    }

    [Fact]
    public async Task SubmitValidation_LoadsHistoryBeforeCalculatingStatus()
    {
        var releaseId = await CreateRelease();
        await Submit(releaseId, "AutomatedTests", "Passed");
        await AssertStatus(releaseId, "Pending", 1);
        await Submit(releaseId, "Performance", "Passed");
        await AssertStatus(releaseId, "ReadyForDeployment", 2);
        await Submit(releaseId, "DeploymentHealth", "Passed");
        await AssertStatus(releaseId, "Verified", 3);
    }

    [Theory]
    [InlineData("AutomatedTests")]
    [InlineData("Performance")]
    [InlineData("DeploymentHealth")]
    public async Task SubmitValidation_AnyFailedOutcome_ReturnsFailedStatus(string failedType)
    {
        var releaseId = await CreateRelease();
        foreach (var type in new[] { "AutomatedTests", "Performance", "DeploymentHealth" })
        {
            await Submit(releaseId, type, type == failedType ? "Failed" : "Passed");
        }
        await AssertStatus(releaseId, "Failed", 3);
    }

    [Theory]
    [InlineData("/services", "{")]
    [InlineData("/services", "null")]
    [InlineData("/services", "[]")]
    [InlineData("/releases", "{")]
    [InlineData("/releases/019a0000-0000-7000-8000-000000000001/validations", "{")]
    public async Task MalformedBodies_ReturnProblemDetails(string path, string json)
    {
        await AssertProblem(await PostJson(path, json), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    public async Task UnexpectedDatabaseError_DoesNotExposeImplementationDetails_InDevelopment(string? accept)
    {
        using var failingFactory = new ApiFactory();
        using var client = failingFactory.CreateClient();
        if (accept is not null)
        {
            client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        }
        using (var scope = failingFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductionReleaseValidationDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"Services\"");
        }

        var response = await client.PostAsJsonAsync("/services", new { name = "failure" });
        await AssertProblem(response, HttpStatusCode.InternalServerError);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SQLite", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no such table", content, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> CreateService()
    {
        var response = await _client.PostAsJsonAsync("/services", new { name = $"service-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJson(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateRelease()
    {
        var serviceId = await CreateService();
        var response = await _client.PostAsJsonAsync("/releases", new { serviceId, version = "1.0", commitSha = "abc123" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJson(response)).GetProperty("id").GetGuid();
    }

    private async Task Submit(Guid releaseId, string type, string outcome)
    {
        var response = await _client.PostAsJsonAsync($"/releases/{releaseId}/validations", new { type, outcome });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task AssertStatus(Guid releaseId, string expected, int historyCount)
    {
        var response = await _client.GetAsync($"/releases/{releaseId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var release = await ReadJson(response);
        Assert.Equal(expected, release.GetProperty("status").GetString());
        Assert.Equal(historyCount, release.GetProperty("validationResults").GetArrayLength());
    }

    private Task<HttpResponseMessage> PostJson(string path, string json) =>
        _client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJson(response);
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
    }
}
