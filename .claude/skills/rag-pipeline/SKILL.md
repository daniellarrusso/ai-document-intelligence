---
name: rag-pipeline
description: Document indexing and question answering (RAG) using Ollama embeddings and pgvector. Use when changing chunking, embeddings, the document indexer, the ask endpoint, vector search, or the embedding model/dimensions.
---

# RAG (question answering over a document)

## Indexing
Text comes from `PdfDocumentTextExtractor` using PdfPig's `ContentOrderTextExtractor` (layout analysis: words and line breaks rebuilt from letter positions), falling back to `page.Text` if that throws on a page. Do not switch to plain `page.Text`: it is only the letters in drawing order, so lines and words run together ("1234567Claimant", "ADAMS50 REDVERS") and claim matching breaks. Documents extracted before this change keep their old glued text until reprocessed. PDFs without a text layer (scans) yield no text; that would need OCR.

After summarising, `LocalDocumentProcessingWorker` calls `IDocumentIndexer` (status `IndexingDocument`), which splits `ExtractedText` with `TextChunker`, embeds the chunks via `IEmbeddingGenerator` (Ollama) and stores them in `DocumentChunks`. Best-effort: a failure leaves the document `Completed` but unqueryable.

## Querying
`POST /api/documents/{id}/ask` with `{ "question": "..." }` -> `IDocumentQuestionAnswerer` embeds the question, takes the top 5 chunks by cosine distance (pgvector) for that document and asks the chat model to answer only from them. Returns `{ answer, sources[] }`; 409 if the document has no indexed chunks, 503 if Ollama is unavailable.

## Prerequisites
- Postgres runs the `pgvector/pgvector:pg16` image (see `docker-compose.yml`).
- The embedding model must be pulled locally: `ollama pull nomic-embed-text` (configured as `Ollama:EmbeddingModel`).

## Constraints
- **Embedding size is fixed at 768** (`EmbeddingDefaults.Dimensions`, matching `nomic-embed-text`). Switching to a model with a different size needs a migration for the column and a re-index of existing documents.
- Documents uploaded before this feature have no chunks until they are re-processed.
- The `DocumentChunks` table has an HNSW cosine index (migration `AddDocumentChunks`).
