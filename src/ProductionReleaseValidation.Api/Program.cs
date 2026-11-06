using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using ProductionReleaseValidation.Api.Endpoints;
using ProductionReleaseValidation.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Connection string 'Postgres' is not configured.");

builder.Services.AddDbContext<ProductionReleaseValidationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(context =>
    Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
        title: "An unexpected error occurred.").ExecuteAsync(context)));
app.UseStatusCodePages(context =>
    Results.Problem(statusCode: context.HttpContext.Response.StatusCode)
        .ExecuteAsync(context.HttpContext));
app.MapReleaseEndpoints();

app.Run();

public partial class Program;
