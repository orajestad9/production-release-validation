using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionReleaseValidation.Api.Domain;

namespace ProductionReleaseValidation.Api.Infrastructure.Persistence.Configurations;

public class ValidationResultConfiguration : IEntityTypeConfiguration<ValidationResult>
{
    public void Configure(EntityTypeBuilder<ValidationResult> builder)
    {
        builder.ToTable("ValidationResults");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.ReleaseId).IsRequired();
        builder.Property(v => v.RecordedAtUtc).IsRequired();
        builder.Property(v => v.Type).IsRequired();
        builder.Property(v => v.Outcome).IsRequired();
        builder.Property(v => v.Summary).HasMaxLength(500);
        builder.Property(v => v.EvidenceUrl).HasMaxLength(2048);

        builder.HasOne<Release>()
            .WithMany(r => r.ValidationResults)
            .HasForeignKey(v => v.ReleaseId)
            .IsRequired();
        
        builder.HasIndex(v => new { v.ReleaseId, v.Type })
            .IsUnique();
    }
}