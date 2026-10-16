import { Claim } from '../claims/claim.model';

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

/** True until the document reaches a terminal status (Completed or Failed). */
export function isDocumentInProgress(status: DocumentStatus): boolean {
  return status !== DocumentStatus.Completed && status !== DocumentStatus.Failed;
}

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
  /** How many claims the document is linked to; 0 means unmatched. Only present on list responses. */
  claimCount?: number;
}

/** Mirrors the backend MatchMethod enum (serialised as an integer): what kind of evidence tied a document to a claim. */
export enum MatchMethod {
  Reference = 0,
  PolicyNumber = 1,
  ClaimantName = 2,
  Ai = 3,
}

const MATCH_METHOD_LABELS: Record<MatchMethod, string> = {
  [MatchMethod.Reference]: 'Claim reference',
  [MatchMethod.PolicyNumber]: 'Policy number',
  [MatchMethod.ClaimantName]: 'Claimant name',
  [MatchMethod.Ai]: 'AI suggestion',
};

export function matchMethodLabel(method: MatchMethod): string {
  return MATCH_METHOD_LABELS[method] ?? 'Match';
}

/** A claim the document appears to belong to, and why. */
export interface ClaimMatch {
  claim: Claim;
  method: MatchMethod;
  detail: string;
}

/** A current link; `supported` is false when the document's text never mentions the claim. */
export interface LinkedClaimCheck {
  claim: Claim;
  supported: boolean;
  method: MatchMethod | null;
  detail: string | null;
}

export interface MatchScan {
  /** False while the document has no extracted text to check. */
  textAvailable: boolean;
  suggestions: ClaimMatch[];
  linkedClaims: LinkedClaimCheck[];
  aiAttempted: boolean;
  aiFailed: boolean;
}

export interface DocumentQuery {
  /** Case-insensitive match on the file name. */
  search?: string;
  /** Only documents that are linked to no claim. */
  unmatched?: boolean;
  pageNumber: number;
  pageSize: number;
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
