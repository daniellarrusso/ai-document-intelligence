import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, afterRenderEffect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CLAIM_STATUSES, Claim, ClaimStatus, claimStatusLabel } from '../claim.model';
import { ClaimService } from '../claim.service';

const PAGE_SIZE = 10;
const SEARCH_DEBOUNCE_MS = 300;

@Component({
  selector: 'app-claim-list',
  imports: [DatePipe, DecimalPipe, RouterLink],
  templateUrl: './claim-list.html',
})
export class ClaimList {
  private readonly claimService = inject(ClaimService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  // Guards against an older, slower response overwriting a newer one while the user is typing.
  private requestId = 0;

  protected readonly statuses = CLAIM_STATUSES;
  protected readonly claims = signal<Claim[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly search = signal('');
  protected readonly status = signal<ClaimStatus | null>(null);
  protected readonly page = signal(1);
  protected readonly hasNextPage = signal(false);

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));

    // Browser only (avoids calling the API during SSR). Runs once initially,
    // then again whenever a claim is created.
    afterRenderEffect(() => {
      this.claimService.created();
      untracked(() => this.load());
    });
  }

  load(): void {
    const requestId = ++this.requestId;
    this.loading.set(true);
    this.error.set('');

    this.claimService
      .getClaims({
        search: this.search().trim(),
        status: this.status(),
        pageNumber: this.page(),
        pageSize: PAGE_SIZE,
      })
      .subscribe({
        next: (claims) => {
          if (requestId !== this.requestId) return;
          this.claims.set(claims);
          // The API returns a bare array, so a full page is the only hint that another may follow.
          this.hasNextPage.set(claims.length === PAGE_SIZE);
          this.loading.set(false);
        },
        error: () => {
          if (requestId !== this.requestId) return;
          this.error.set('Unable to load claims.');
          this.loading.set(false);
        },
      });
  }

  protected onSearchInput(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);

    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.resetAndLoad(), SEARCH_DEBOUNCE_MS);
  }

  protected onStatusChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.status.set(value === '' ? null : (Number(value) as ClaimStatus));
    this.resetAndLoad();
  }

  protected previousPage(): void {
    this.page.update((page) => Math.max(1, page - 1));
    this.load();
  }

  protected nextPage(): void {
    this.page.update((page) => page + 1);
    this.load();
  }

  protected isFiltered(): boolean {
    return this.search().trim() !== '' || this.status() !== null;
  }

  protected statusLabel(status: ClaimStatus): string {
    return claimStatusLabel(status);
  }

  private resetAndLoad(): void {
    clearTimeout(this.searchTimer);
    this.page.set(1);
    this.load();
  }
}
