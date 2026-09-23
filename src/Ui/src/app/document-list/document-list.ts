import { DatePipe } from '@angular/common';
import { Component, DestroyRef, afterRenderEffect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DocumentStatus, DocumentSummary } from './document.model';
import { DocumentService } from './document.service';

const POLL_INTERVAL_MS = 2000;

@Component({
  selector: 'app-document-list',
  imports: [DatePipe, RouterLink],
  templateUrl: './document-list.html',
  styleUrl: './document-list.css',
})
export class DocumentList {
  private readonly documentService = inject(DocumentService);
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly documents = signal<DocumentSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pollTimer));

    // Browser only (avoids calling the API during SSR). Runs once initially,
    // then again whenever a document is uploaded.
    afterRenderEffect(() => {
      this.documentService.uploads();
      untracked(() => this.load());
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');

    this.documentService.getDocuments().subscribe({
      next: (documents) => {
        this.documents.set(documents);
        this.loading.set(false);
        this.schedulePollIfProcessing(documents);
      },
      error: () => {
        this.error.set('Unable to load documents.');
        this.loading.set(false);
      },
    });
  }

  // Refresh until in-flight documents reach a terminal status (Completed/Failed).
  private schedulePollIfProcessing(documents: DocumentSummary[]): void {
    clearTimeout(this.pollTimer);
    const inFlight = documents.some(
      (d) => d.status === DocumentStatus.Uploaded || d.status === DocumentStatus.Processing,
    );
    if (inFlight) {
      this.pollTimer = setTimeout(() => this.load(), POLL_INTERVAL_MS);
    }
  }

  protected statusLabel(status: DocumentStatus): string {
    return DocumentStatus[status] ?? 'Unknown';
  }

  protected formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
