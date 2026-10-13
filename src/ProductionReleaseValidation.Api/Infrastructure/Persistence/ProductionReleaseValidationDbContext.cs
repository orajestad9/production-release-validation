using Microsoft.EntityFrameworkCore;
using ProductionReleaseValidation.Api.Domain;

namespace ProductionReleaseValidation.Api.Infrastructure.Persistence;

public class ProductionReleaseValidationDbContext(
    DbContextOptions<ProductionReleaseValidationDbContext> options)
    : DbContext(options)
{
    public DbSet<Service> Services => Set<Service>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ProductionReleaseValidationDbContext).Assembly);
    }
}