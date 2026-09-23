// Mirrors AiDocumentIntelligence.Domain.DocumentStatus (serialised as a number by the API).
export enum DocumentStatus {
  Uploaded = 0,
  Processing = 1,
  Completed = 2,
  Failed = 3,
}

export interface DocumentSummary {
  id: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  status: DocumentStatus;
  uploadedAt: string;
  processedAt: string | null;
}
