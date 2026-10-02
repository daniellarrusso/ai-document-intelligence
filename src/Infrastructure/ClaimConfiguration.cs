using AiDocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiDocumentIntelligence.Infrastructure;

public class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public void Configure(EntityTypeBuilder<Claim> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Reference).IsRequired().HasMaxLength(50);
        builder.Property(c => c.PolicyNumber).IsRequired().HasMaxLength(50);
        builder.Property(c => c.ClaimantName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.AssignedTo).HasMaxLength(200);
        builder.Property(c => c.AmountClaimed).HasPrecision(18, 2);

        builder.HasIndex(c => c.Reference).IsUnique();

        // Restrict: deleting a claim must not silently delete its documents.
        builder.HasMany(c => c.Documents)
            .WithOne()
            .HasForeignKey(d => d.ClaimId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
