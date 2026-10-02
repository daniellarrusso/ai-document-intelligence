namespace AiDocumentIntelligence.Infrastructure;

public static class EmbeddingDefaults
{
    // Output size of the default embedding model (nomic-embed-text). The vector column is fixed-width,
    // so changing the embedding model to one with a different size needs a migration and a re-index.
    public const int Dimensions = 768;
}
