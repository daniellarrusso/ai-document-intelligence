import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, ElementRef, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DocumentStatus, documentStatusLabel, isDocumentInProgress } from '../../document-list/document.model';
import { formatFileSize } from '../../shared/format-file-size';
import { UserService } from '../../auth/user.service';
import { ClaimDetail, ClaimStatus, Handler, claimStatusLabel } from '../claim.model';
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
  private readonly router = inject(Router);
  protected readonly user = inject(UserService);
  private pollTimer: ReturnType<typeof setTimeout> | undefined;
  // Set after an upload until a refresh succeeds, so a failed refresh is retried even if nothing was processing before.
  private refreshPending = false;

  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly detail = signal<ClaimDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  protected readonly selectedFile = signal<File | null>(null);
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal('');

  protected readonly handlers = signal<Handler[]>([]);
  protected readonly handlersError = signal(false);
  /** The handler chosen in the dropdown but not yet saved; null means "unchanged". */
  protected readonly pendingAssignee = signal<string | null>(null);
  protected readonly assigning = signal(false);
  protected readonly assignError = signal('');
  protected readonly privacyError = signal('');

  protected readonly selectedAssignee = computed(
    () => this.pendingAssignee() ?? this.detail()?.claim.assignedToUserId ?? '',
  );

  protected readonly canReassign = computed(() => {
    const pending = this.pendingAssignee();
    return pending !== null && pending !== '' && pending !== this.detail()?.claim.assignedToUserId && !this.assigning();
  });

  /** The handlers, plus the current assignee if they are not in that list so the dropdown can still show them. */
  protected readonly assigneeOptions = computed<Handler[]>(() => {
    const handlers = this.handlers();
    const claim = this.detail()?.claim;
    if (claim?.assignedToUserId && !handlers.some((h) => h.id === claim.assignedToUserId)) {
      return [{ id: claim.assignedToUserId, displayName: claim.assignedToName ?? 'Current assignee' }, ...handlers];
    }
    return handlers;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.pollTimer));

    // Browser only (avoids calling the API during SSR).
    afterNextRender(() => {
      this.load();
      if (this.user.canWrite()) {
        this.loadHandlers();
      }
    });
  }

  private loadHandlers(): void {
    this.claimService.getHandlers().subscribe({
      next: (handlers) => {
        this.handlers.set(handlers);
        this.handlersError.set(false);
      },
      error: () => this.handlersError.set(true),
    });
  }

  protected onAssigneeChange(event: Event): void {
    this.pendingAssignee.set((event.target as HTMLSelectElement).value);
  }

  protected reassign(): void {
    const claimId = this.detail()?.claim.id;
    const assigneeId = this.pendingAssignee();
    if (!claimId || !assigneeId || !this.canReassign()) {
      return;
    }

    this.assigning.set(true);
    this.assignError.set('');

    this.claimService.assignClaim(claimId, assigneeId).subscribe({
      next: () => {
        this.assigning.set(false);
        this.pendingAssignee.set(null);
        if (this.user.isAdmin()) {
          this.load();
        } else {
          // A handler can only see their own claims, so the claim they just gave away is no longer theirs to open.
          void this.router.navigateByUrl('/claims');
        }
      },
      error: (err: HttpErrorResponse) => {
        this.assigning.set(false);
        this.assignError.set(
          err.status === 400 && typeof err.error === 'string' && err.error
            ? err.error
            : 'Unable to reassign the claim.',
        );
      },
    });
  }

  protected togglePrivate(event: Event): void {
    const claimId = this.detail()?.claim.id;
    if (!claimId) {
      return;
    }

    this.privacyError.set('');
    this.claimService.setPrivacy(claimId, (event.target as HTMLInputElement).checked).subscribe({
      next: () => this.load(),
      error: () => {
        this.privacyError.set('Unable to change the claim privacy.');
        this.load();
      },
    });
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
        this.refreshPending = false;
        this.detail.set(detail);
        this.loading.set(false);
        this.schedulePollIfInProgress(detail);
      },
      error: (err: { status?: number }) => {
        const current = this.detail();
        if (current && err.status !== 404) {
          // A failed background refresh shouldn't replace a page that is already showing; try again shortly.
          this.schedulePollIfInProgress(current);
          if (this.refreshPending && this.pollTimer === undefined) {
            this.pollTimer = setTimeout(() => this.load(), POLL_INTERVAL_MS);
          }
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
    this.pollTimer = undefined;
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
        this.refreshPending = true;
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
