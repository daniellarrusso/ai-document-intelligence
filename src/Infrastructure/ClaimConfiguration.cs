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
        builder.Property(c => c.AmountClaimed).HasPrecision(Claim.AmountPrecision, Claim.AmountScale);

        builder.Property(c => c.IsPrivate).HasDefaultValue(false);

        builder.HasIndex(c => c.Reference).IsUnique();
        builder.HasIndex(c => c.AssignedToUserId);

        // SetNull: removing a user must not delete or block their claims.
        builder.HasOne(c => c.AssignedToUser)
            .WithMany()
            .HasForeignKey(c => c.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict: deleting a claim must not silently delete its documents.
        builder.HasMany(c => c.Documents)
            .WithOne()
            .HasForeignKey(d => d.ClaimId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
