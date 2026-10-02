using AiDocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiDocumentIntelligence.Infrastructure;

public class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text).IsRequired();
        builder.Property(c => c.Embedding).HasColumnType($"vector({EmbeddingDefaults.Dimensions})");

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex }).IsUnique();

        // Approximate nearest-neighbour index for cosine distance, matching the CosineDistance ordering used in search.
        builder.HasIndex(c => c.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");
    }
}
