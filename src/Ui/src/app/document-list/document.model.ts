// Mirrors AiDocumentIntelligence.Domain.DocumentStatus (serialised as a number by the API).
// New values are appended, matching the backend enum, since the underlying number is persisted.
export enum DocumentStatus {
  Uploaded = 0,
  Processing = 1,
  Completed = 2,
  Failed = 3,
  ExtractingText = 4,
  GeneratingSummary = 5,
}

const STATUS_LABELS: Record<DocumentStatus, string> = {
  [DocumentStatus.Uploaded]: 'Uploaded',
  [DocumentStatus.Processing]: 'Processing document...',
  [DocumentStatus.ExtractingText]: 'Extracting text...',
  [DocumentStatus.GeneratingSummary]: 'Generating summary...',
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
