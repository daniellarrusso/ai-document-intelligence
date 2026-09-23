import { DatePipe } from '@angular/common';
import { Component, afterNextRender, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DocumentStatus, DocumentSummary } from '../document-list/document.model';
import { DocumentService } from '../document-list/document.service';

@Component({
  selector: 'app-document-details',
  imports: [DatePipe, RouterLink],
  templateUrl: './document-details.html',
})
export class DocumentDetails {
  private readonly documentService = inject(DocumentService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly document = signal<DocumentSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal('');

  constructor() {
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
      },
      error: (err: { status?: number }) => {
        this.error.set(err.status === 404 ? 'Document not found.' : 'Unable to load document.');
        this.loading.set(false);
      },
    });
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
    return DocumentStatus[status] ?? 'Unknown';
  }

  protected formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
