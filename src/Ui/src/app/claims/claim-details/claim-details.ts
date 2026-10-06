import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DocumentStatus, documentStatusLabel, isDocumentInProgress } from '../../document-list/document.model';
import { formatFileSize } from '../../shared/format-file-size';
import { UserService } from '../../auth/user.service';
import { ClaimDetail, ClaimStatus, claimStatusLabel } from '../claim.model';
import { ClaimService } from '../claim.service';

const POLL_INTERVAL_MS = 2000;

@Component({
  selector: 'app-claim-details',
  imports: [DatePipe, DecimalPipe, RouterLink],
  templateUrl: './claim-details.html',
})
export class ClaimDetails {
  private readonly claimService = inject(ClaimService);
  private readonly route = inject(ActivatedRoute);
  protected readonly user = inject(UserService);
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly detail = signal<ClaimDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  protected readonly selectedFile = signal<File | null>(null);
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal('');

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pollTimer));

    // Browser only (avoids calling the API during SSR).
    afterNextRender(() => this.load());
  }

  private load(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.error.set('Claim not found.');
      this.loading.set(false);
      return;
    }

    this.claimService.getClaim(id).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.loading.set(false);
        this.schedulePollIfInProgress(detail);
      },
      error: (err: { status?: number }) => {
        const current = this.detail();
        if (current && err.status !== 404) {
          // A failed background refresh shouldn't replace a page that is already showing; try again shortly.
          this.schedulePollIfInProgress(current);
          return;
        }

        this.error.set(err.status === 404 ? 'Claim not found.' : 'Unable to load claim.');
        this.detail.set(null);
        this.loading.set(false);
      },
    });
  }

  // Refresh until every document has reached a terminal status (Completed/Failed).
  private schedulePollIfInProgress(detail: ClaimDetail): void {
    clearTimeout(this.pollTimer);
    if (detail.documents.some((d) => isDocumentInProgress(d.status))) {
      this.pollTimer = setTimeout(() => this.load(), POLL_INTERVAL_MS);
    }
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile.set(input.files?.[0] ?? null);
  }

  protected upload(): void {
    const file = this.selectedFile();
    const claimId = this.detail()?.claim.id;
    if (!file || !claimId || this.uploading()) {
      return;
    }

    this.uploading.set(true);
    this.uploadError.set('');

    this.claimService.uploadDocument(claimId, file).subscribe({
      next: () => {
        this.selectedFile.set(null);
        const input = this.fileInput();
        if (input) {
          input.nativeElement.value = '';
        }
        this.uploading.set(false);
        this.load();
      },
      error: () => {
        this.uploadError.set('File upload failed!');
        this.uploading.set(false);
      },
    });
  }

  protected claimStatusLabel(status: ClaimStatus): string {
    return claimStatusLabel(status);
  }

  protected statusLabel(status: DocumentStatus): string {
    return documentStatusLabel(status);
  }

  protected formatSize(bytes: number): string {
    return formatFileSize(bytes);
  }
}
