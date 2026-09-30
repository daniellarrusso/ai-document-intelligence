import { DatePipe } from '@angular/common';
import { Component, DestroyRef, afterNextRender, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  DocumentStatus,
  DocumentSummary,
  PIPELINE_STATUSES,
  documentStatusLabel,
} from '../document-list/document.model';
import { DocumentService } from '../document-list/document.service';

const POLL_INTERVAL_MS = 2000;

export type StepState = 'done' | 'current' | 'pending';

@Component({
  selector: 'app-document-details',
  imports: [DatePipe, RouterLink],
  templateUrl: './document-details.html',
})
export class DocumentDetails {
  private readonly documentService = inject(DocumentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly DocumentStatus = DocumentStatus;

  protected readonly document = signal<DocumentSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal('');

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
      next: () => void this.router.navigateByUrl('/'),
      error: () => {
        this.deleteError.set('Unable to delete the document. Please try again.');
        this.deleting.set(false);
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
