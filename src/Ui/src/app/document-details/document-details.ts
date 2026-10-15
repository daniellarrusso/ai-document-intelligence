import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, afterNextRender, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  AnswerSource,
  DocumentStatus,
  DocumentSummary,
  PIPELINE_STATUSES,
  documentStatusLabel,
} from '../document-list/document.model';
import { DocumentService } from '../document-list/document.service';
import { UserService } from '../auth/user.service';

const POLL_INTERVAL_MS = 2000;
const MAX_QUESTION_LENGTH = 1000;

export type StepState = 'done' | 'current' | 'pending';

@Component({
  selector: 'app-document-details',
  imports: [DatePipe, DecimalPipe, RouterLink],
  templateUrl: './document-details.html',
})
export class DocumentDetails {
  private readonly documentService = inject(DocumentService);
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

  // Documents that belong to a claim lead back to it rather than to the global document list.
  protected readonly backLink = computed(() => {
    const claimId = this.document()?.claimId;
    return claimId
      ? { commands: ['/claims', claimId], label: 'Back to claim' }
      : { commands: ['/'], label: 'Back to documents' };
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
    afterNextRender(() => this.load());
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
      },
      error: (err: { status?: number }) => {
        this.error.set(err.status === 404 ? 'Document not found.' : 'Unable to load document.');
        this.loading.set(false);
      },
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
      next: () => void this.router.navigateByUrl(document.claimId ? `/claims/${document.claimId}` : '/'),
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
