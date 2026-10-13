using Microsoft.EntityFrameworkCore;
using ProductionReleaseValidation.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Connection string 'Postgres' is not configured.");

builder.Services.AddDbContext<ProductionReleaseValidationDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

app.Run();
