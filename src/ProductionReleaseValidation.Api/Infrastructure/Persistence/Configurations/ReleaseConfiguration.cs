using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionReleaseValidation.Api.Domain;

namespace ProductionReleaseValidation.Api.Infrastructure.Persistence.Configurations;

public class ReleaseConfiguration : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> builder)
    {
        builder.ToTable("Releases");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ServiceId).IsRequired();
        builder.Property(r => r.Version).IsRequired().HasMaxLength(50);
        builder.Property(r => r.CommitSha).IsRequired().HasMaxLength(64);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Ignore(r => r.Status);

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(r => r.ServiceId)
            .IsRequired();
        
        builder.HasIndex(r => new { r.ServiceId, r.Version })
            .IsUnique();
    }
}