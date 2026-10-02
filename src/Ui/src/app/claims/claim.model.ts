/** Mirrors the backend ClaimStatus enum, which is serialised as an integer. */
export enum ClaimStatus {
  Open = 0,
  InReview = 1,
  AwaitingApproval = 2,
  Approved = 3,
  Rejected = 4,
  Closed = 5,
}

const CLAIM_STATUS_LABELS: Record<ClaimStatus, string> = {
  [ClaimStatus.Open]: 'Open',
  [ClaimStatus.InReview]: 'In review',
  [ClaimStatus.AwaitingApproval]: 'Awaiting approval',
  [ClaimStatus.Approved]: 'Approved',
  [ClaimStatus.Rejected]: 'Rejected',
  [ClaimStatus.Closed]: 'Closed',
};

export const CLAIM_STATUSES: readonly ClaimStatus[] = [
  ClaimStatus.Open,
  ClaimStatus.InReview,
  ClaimStatus.AwaitingApproval,
  ClaimStatus.Approved,
  ClaimStatus.Rejected,
  ClaimStatus.Closed,
];

export function claimStatusLabel(status: ClaimStatus): string {
  return CLAIM_STATUS_LABELS[status] ?? 'Unknown';
}

/** Limits enforced by the API (see Claim.cs); the form mirrors them for faster feedback. */
export const CLAIM_LIMITS = { policyNumber: 50, name: 200 } as const;

export interface Claim {
  id: string;
  reference: string;
  policyNumber: string;
  claimantName: string;
  /** Date only, `yyyy-MM-dd`. */
  incidentDate: string;
  amountClaimed: number;
  status: ClaimStatus;
  assignedTo: string | null;
  createdAt: string;
}

export interface CreateClaimRequest {
  policyNumber: string;
  claimantName: string;
  /** Date only, `yyyy-MM-dd`. */
  incidentDate: string;
  amountClaimed: number;
  assignedTo: string | null;
}
