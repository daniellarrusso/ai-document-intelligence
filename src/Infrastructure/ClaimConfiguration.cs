using AiDocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiDocumentIntelligence.Infrastructure;

public class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public void Configure(EntityTypeBuilder<Claim> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Reference).IsRequired().HasMaxLength(Claim.MaxReferenceLength);
        builder.Property(c => c.PolicyNumber).IsRequired().HasMaxLength(Claim.MaxPolicyNumberLength);
        builder.Property(c => c.ClaimantName).IsRequired().HasMaxLength(Claim.MaxNameLength);
        builder.Property(c => c.AssignedTo).HasMaxLength(Claim.MaxNameLength);
        builder.Property(c => c.AmountClaimed).HasPrecision(Claim.AmountPrecision, Claim.AmountScale);

        builder.HasIndex(c => c.Reference).IsUnique();

        // Restrict: deleting a claim must not silently delete its documents.
        builder.HasMany(c => c.Documents)
            .WithOne()
            .HasForeignKey(d => d.ClaimId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
