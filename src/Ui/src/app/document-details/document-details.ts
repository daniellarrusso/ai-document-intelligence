import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, afterNextRender, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { Claim } from '../claims/claim.model';
import { ClaimService } from '../claims/claim.service';
import {
  AnswerSource,
  DocumentStatus,
  DocumentSummary,
  MatchMethod,
  MatchScan,
  PIPELINE_STATUSES,
  documentStatusLabel,
  matchMethodLabel,
} from '../document-list/document.model';
import { DocumentService } from '../document-list/document.service';
import { UserService } from '../auth/user.service';

const POLL_INTERVAL_MS = 2000;
const MAX_QUESTION_LENGTH = 1000;
const CLAIM_SEARCH_PAGE_SIZE = 10;

export type StepState = 'done' | 'current' | 'pending';

@Component({
  selector: 'app-document-details',
  imports: [DatePipe, DecimalPipe, RouterLink],
  templateUrl: './document-details.html',
})
export class DocumentDetails {
  private readonly documentService = inject(DocumentService);
  private readonly claimService = inject(ClaimService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly user = inject(UserService);
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly DocumentStatus = DocumentStatus;

  protected readonly document = signal<DocumentSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal('');

  // Opened from a claim (the claim page adds ?claim=<id>), the page leads back to that claim rather than to
  // the global document list.
  private readonly fromClaimId = this.route.snapshot.queryParamMap.get('claim');
  protected readonly backLink = this.fromClaimId
    ? { url: `/claims/${this.fromClaimId}`, label: 'Back to claim' }
    : { url: '/', label: 'Back to documents' };

  /** The claims this document is linked to that the caller can see; empty means it is unmatched. */
  protected readonly linkedClaims = signal<Claim[]>([]);
  protected readonly claimsLoaded = signal(false);
  protected readonly claimSearch = signal('');
  /** Search results (claims the caller can see); null until a search has been run. */
  protected readonly claimResults = signal<Claim[] | null>(null);
  protected readonly claimSearching = signal(false);
  protected readonly claimSearchError = signal('');
  protected readonly linkingIds = signal<ReadonlySet<string>>(new Set());
  protected readonly linkError = signal('');

  // Why this page may look for matches by itself: right after an upload ('upload', asks for confirmation in a
  // dialog) or when opened from the list's "Match to claim" link ('match', shows the results inline).
  private readonly autoScanMode: 'upload' | 'match' | null =
    this.route.snapshot.queryParamMap.get('uploaded') === '1'
      ? 'upload'
      : this.route.snapshot.queryParamMap.get('match') === '1'
        ? 'match'
        : null;
  private autoScanStarted = false;

  protected readonly scan = signal<MatchScan | null>(null);
  protected readonly scanning = signal(false);
  protected readonly scanError = signal('');
  protected readonly confirmingMatch = signal(false);
  /** Linked claims the user chose to keep even though the text doesn't mention them. */
  protected readonly keptClaimIds = signal<ReadonlySet<string>>(new Set());

  protected readonly canScan = computed(() => this.user.canWrite() && this.document()?.status === DocumentStatus.Completed);

  /** Current links whose claim the document's text never mentions, which the user hasn't chosen to keep. */
  protected readonly unsupportedLinks = computed(() =>
    (this.scan()?.linkedClaims ?? []).filter((l) => !l.supported && !this.keptClaimIds().has(l.claim.id)),
  );

  /** Search results minus the claims the document is already linked to. */
  protected readonly linkableClaims = computed(() => {
    const linked = new Set(this.linkedClaims().map((c) => c.id));
    return (this.claimResults() ?? []).filter((c) => !linked.has(c.id));
  });

  protected readonly maxQuestionLength = MAX_QUESTION_LENGTH;
  protected readonly question = signal('');
  protected readonly asking = signal(false);
  protected readonly answer = signal<string | null>(null);
  protected readonly sources = signal<AnswerSource[]>([]);
  protected readonly askError = signal('');
  protected readonly canAsk = computed(() => this.question().trim().length > 0 && !this.asking());

  // Progress stepper: pending while extraction/summarization run, hidden once terminal (Completed/Failed).
  protected readonly steps = computed(() => {
    const doc = this.document();
    if (!doc || !this.isInProgress(doc.status)) {
      return [];
    }

    const currentIndex = Math.max(PIPELINE_STATUSES.indexOf(doc.status), 0);
    return PIPELINE_STATUSES.map((status, index) => ({
      label: documentStatusLabel(status),
      state: (index < currentIndex ? 'done' : index === currentIndex ? 'current' : 'pending') as StepState,
    }));
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pollTimer));

    // Browser only (avoids calling the API during SSR).
    afterNextRender(() => {
      this.load();
      this.loadClaims();
    });
  }

  private load(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.error.set('Document not found.');
      this.loading.set(false);
      return;
    }

    this.documentService.getDocument(id).subscribe({
      next: (document) => {
        this.document.set(document);
        this.loading.set(false);
        this.schedulePollIfInProgress(document);
        this.maybeAutoScan();
      },
      error: (err: { status?: number }) => {
        this.error.set(err.status === 404 ? 'Document not found.' : 'Unable to load document.');
        this.loading.set(false);
      },
    });
  }

  private loadClaims(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }

    this.documentService.getClaims(id).subscribe({
      next: (claims) => {
        this.linkedClaims.set(claims);
        this.claimsLoaded.set(true);
        this.maybeAutoScan();
      },
      error: (err: { status?: number }) => {
        if (err.status === 404) {
          // After unlinking, a handler may no longer be allowed to see this document at all.
          void this.router.navigateByUrl(this.backLink.url);
        }
      },
    });
  }

  // Once the document has been processed (and we know its links), look for matches a single time.
  private maybeAutoScan(): void {
    if (
      !this.autoScanMode ||
      this.autoScanStarted ||
      !this.canScan() ||
      !this.claimsLoaded()
    ) {
      return;
    }

    this.autoScanStarted = true;
    // Ask the AI too, but only if the cheap checks find nothing.
    this.runScan(false, (first) => {
      if (first.suggestions.length === 0 && first.textAvailable) {
        this.runScan(true, (second) => this.offerSuggestions(second));
      } else {
        this.offerSuggestions(first);
      }
    });
  }

  private offerSuggestions(scan: MatchScan): void {
    if (this.autoScanMode === 'upload' && scan.suggestions.length > 0) {
      this.confirmingMatch.set(true);
    }
  }

  /** Looks at the document's text for claims it belongs to and checks its current links. */
  protected scanForMatches(useAi = false): void {
    this.runScan(useAi);
  }

  private runScan(useAi: boolean, onDone?: (scan: MatchScan) => void): void {
    const id = this.document()?.id;
    if (!id || this.scanning()) {
      return;
    }

    this.scanning.set(true);
    this.scanError.set('');

    this.documentService.getMatchSuggestions(id, useAi).subscribe({
      next: (scan) => {
        this.scan.set(scan);
        this.scanning.set(false);
        onDone?.(scan);
      },
      error: () => {
        this.scanError.set('Unable to scan the document for matching claims.');
        this.scanning.set(false);
      },
    });
  }

  protected closeMatchDialog(): void {
    this.confirmingMatch.set(false);
  }

  protected keepLink(claimId: string): void {
    this.keptClaimIds.update((ids) => new Set(ids).add(claimId));
  }

  protected matchLabel(method: MatchMethod): string {
    return matchMethodLabel(method);
  }

  protected onClaimSearchInput(event: Event): void {
    this.claimSearch.set((event.target as HTMLInputElement).value);
  }

  /** Finds claims by reference, policy number or claimant name. The API only returns claims the caller can see. */
  protected searchClaims(): void {
    if (this.claimSearching()) {
      return;
    }

    this.claimSearching.set(true);
    this.claimSearchError.set('');

    this.claimService
      .getClaims({ search: this.claimSearch().trim(), pageNumber: 1, pageSize: CLAIM_SEARCH_PAGE_SIZE })
      .subscribe({
        next: (claims) => {
          this.claimResults.set(claims);
          this.claimSearching.set(false);
        },
        error: () => {
          this.claimSearchError.set('Unable to search claims.');
          this.claimSearching.set(false);
        },
      });
  }

  protected linkToClaim(claimId: string): void {
    this.changeLink(claimId, (documentId) => this.claimService.linkDocument(claimId, documentId), () => {
      // No longer a suggestion; close the dialog once there is nothing left to confirm.
      this.scan.update((scan) =>
        scan ? { ...scan, suggestions: scan.suggestions.filter((m) => m.claim.id !== claimId) } : scan,
      );
      if (this.scan()?.suggestions.length === 0) {
        this.confirmingMatch.set(false);
      }
    });
  }

  protected unlinkFromClaim(claimId: string): void {
    this.changeLink(claimId, (documentId) => this.claimService.unlinkDocument(claimId, documentId), () =>
      this.scan.update((scan) =>
        scan ? { ...scan, linkedClaims: scan.linkedClaims.filter((l) => l.claim.id !== claimId) } : scan,
      ),
    );
  }

  private changeLink(claimId: string, request: (documentId: string) => Observable<void>, onDone?: () => void): void {
    const documentId = this.document()?.id;
    if (!documentId || this.linkingIds().has(claimId)) {
      return;
    }

    this.setLinking(claimId, true);
    this.linkError.set('');

    request(documentId).subscribe({
      next: () => {
        this.setLinking(claimId, false);
        onDone?.();
        this.loadClaims();
      },
      error: (err: HttpErrorResponse) => {
        this.setLinking(claimId, false);
        this.linkError.set(
          err.status === 404
            ? 'That document or claim is no longer available.'
            : err.status === 403
              ? 'You do not have permission to change this claim.'
              : 'Unable to update the claim link.',
        );
      },
    });
  }

  private setLinking(claimId: string, linking: boolean): void {
    this.linkingIds.update((ids) => {
      const next = new Set(ids);
      if (linking) {
        next.add(claimId);
      } else {
        next.delete(claimId);
      }
      return next;
    });
  }

  // Refresh until the document reaches a terminal status (Completed/Failed).
  private schedulePollIfInProgress(document: DocumentSummary): void {
    clearTimeout(this.pollTimer);
    if (this.isInProgress(document.status)) {
      this.pollTimer = setTimeout(() => this.load(), POLL_INTERVAL_MS);
    }
  }

  protected isInProgress(status: DocumentStatus): boolean {
    return status !== DocumentStatus.Completed && status !== DocumentStatus.Failed;
  }

  protected openDeleteConfirm(): void {
    this.deleteError.set('');
    this.confirmingDelete.set(true);
  }

  protected cancelDelete(): void {
    if (!this.deleting()) {
      this.confirmingDelete.set(false);
    }
  }

  protected confirmDelete(): void {
    const document = this.document();
    if (!document || this.deleting()) {
      return;
    }

    this.deleting.set(true);
    this.deleteError.set('');

    this.documentService.delete(document.id).subscribe({
      next: () => void this.router.navigateByUrl(this.backLink.url),
      error: () => {
        this.deleteError.set('Unable to delete the document. Please try again.');
        this.deleting.set(false);
      },
    });
  }

  protected onQuestionInput(event: Event): void {
    this.question.set((event.target as HTMLTextAreaElement).value);
  }

  protected ask(): void {
    const document = this.document();
    const question = this.question().trim();
    if (!document || !question || this.asking()) {
      return;
    }

    this.asking.set(true);
    this.askError.set('');
    this.answer.set(null);
    this.sources.set([]);

    this.documentService.ask(document.id, question).subscribe({
      next: (result) => {
        this.answer.set(result.answer);
        this.sources.set(result.sources);
        this.asking.set(false);
      },
      error: (err: { status?: number }) => {
        this.askError.set(
          err.status === 409
            ? 'This document has no searchable content, so it cannot be queried.'
            : 'Unable to get an answer right now. Please try again.',
        );
        this.asking.set(false);
      },
    });
  }

  protected statusLabel(status: DocumentStatus): string {
    return documentStatusLabel(status);
  }

  protected formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
