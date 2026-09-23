import { DatePipe } from '@angular/common';
import { Component, afterRenderEffect, inject, signal, untracked } from '@angular/core';
import { DocumentStatus, DocumentSummary } from './document.model';
import { DocumentService } from './document.service';

@Component({
  selector: 'app-document-list',
  imports: [DatePipe],
  templateUrl: './document-list.html',
  styleUrl: './document-list.css',
})
export class DocumentList {
  private readonly documentService = inject(DocumentService);

  protected readonly documents = signal<DocumentSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  constructor() {
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
      },
      error: () => {
        this.error.set('Unable to load documents.');
        this.loading.set(false);
      },
    });
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
