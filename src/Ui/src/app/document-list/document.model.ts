// Mirrors AiDocumentIntelligence.Domain.DocumentStatus (serialised as a number by the API).
// New values are appended, matching the backend enum, since the underlying number is persisted.
export enum DocumentStatus {
  Uploaded = 0,
  Processing = 1,
  Completed = 2,
  Failed = 3,
  ExtractingText = 4,
  GeneratingSummary = 5,
  IndexingDocument = 6,
}

const STATUS_LABELS: Record<DocumentStatus, string> = {
  [DocumentStatus.Uploaded]: 'Uploaded',
  [DocumentStatus.Processing]: 'Processing document...',
  [DocumentStatus.ExtractingText]: 'Extracting text...',
  [DocumentStatus.GeneratingSummary]: 'Generating summary...',
  [DocumentStatus.IndexingDocument]: 'Indexing for search...',
  [DocumentStatus.Completed]: 'Complete',
  [DocumentStatus.Failed]: 'Failed',
};

export function documentStatusLabel(status: DocumentStatus): string {
  return STATUS_LABELS[status] ?? 'Unknown';
}

// The pipeline's logical order — NOT the same as the enum's numeric order above, which is
// append-only because the values are persisted. Used to drive the progress stepper.
export const PIPELINE_STATUSES: DocumentStatus[] = [
  DocumentStatus.Processing,
  DocumentStatus.ExtractingText,
  DocumentStatus.GeneratingSummary,
  DocumentStatus.IndexingDocument,
  DocumentStatus.Completed,
];

export interface DocumentSummary {
  id: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  status: DocumentStatus;
  uploadedAt: string;
  processedAt: string | null;
  extractedText: string | null;
  summary: string | null;
}

// A retrieved document excerpt that supports an answer. Score is cosine similarity (higher is more relevant).
export interface AnswerSource {
  chunkIndex: number;
  text: string;
  score: number;
}

export interface DocumentAnswer {
  answer: string;
  sources: AnswerSource[];
}
