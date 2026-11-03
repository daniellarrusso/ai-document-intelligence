using AiDocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiDocumentIntelligence.Infrastructure;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.Property(d => d.ContentHash).HasMaxLength(ContentHasher.HexLength);

        // Not unique: the same file can legitimately be held twice (on two claims, say), so finding a copy
        // should be a warning the caller decides about, not a database error.
        builder.HasIndex(d => d.ContentHash);
    }
}
