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

        // Many-to-many through ClaimDocument. Restrict on the claim side: deleting a claim must not silently
        // unlink its documents. Cascade on the document side: deleting a document removes its links.
        builder.HasMany(c => c.Documents)
            .WithMany()
            .UsingEntity<ClaimDocument>(
                link => link.HasOne<Document>().WithMany().HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Cascade),
                link => link.HasOne<Claim>().WithMany().HasForeignKey(l => l.ClaimId).OnDelete(DeleteBehavior.Restrict),
                link =>
                {
                    link.HasKey(l => new { l.ClaimId, l.DocumentId });
                    link.HasIndex(l => l.DocumentId);
                    link.HasOne<AppUser>().WithMany().HasForeignKey(l => l.LinkedByUserId).OnDelete(DeleteBehavior.SetNull);
                });
    }
}
