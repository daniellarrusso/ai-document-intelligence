import { HttpErrorResponse } from '@angular/common/http';
import { Component, WritableSignal, afterNextRender, computed, inject, signal } from '@angular/core';
import { UserService } from '../../auth/user.service';
import { CLAIM_LIMITS, Handler } from '../claim.model';
import { ClaimService } from '../claim.service';

type TextField = 'policyNumber' | 'claimantName' | 'incidentDate' | 'amountClaimed' | 'assignedTo';

function todayIso(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}

@Component({
  selector: 'app-create-claim',
  templateUrl: './create-claim.html',
})
export class CreateClaim {
  private readonly claimService = inject(ClaimService);
  private readonly user = inject(UserService);

  protected readonly limits = CLAIM_LIMITS;
  protected readonly today = todayIso();

  protected readonly policyNumber = signal('');
  protected readonly claimantName = signal('');
  protected readonly incidentDate = signal('');
  protected readonly amountClaimed = signal('');
  /** The chosen handler's id; blank means the creator (the default). */
  protected readonly assignedTo = signal('');
  protected readonly isPrivate = signal(false);

  private readonly allHandlers = signal<Handler[]>([]);
  protected readonly handlersError = signal(false);

  /** Other handlers to assign to; the creator is the blank "Me" option, so they are left out of this list. */
  protected readonly handlers = computed(() => {
    const me = this.user.user()?.id;
    return this.allHandlers().filter((handler) => handler.id !== me);
  });

  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly created = signal('');

  private readonly fields: Record<TextField, WritableSignal<string>> = {
    policyNumber: this.policyNumber,
    claimantName: this.claimantName,
    incidentDate: this.incidentDate,
    amountClaimed: this.amountClaimed,
    assignedTo: this.assignedTo,
  };

  protected readonly valid = computed(() => {
    const policyNumber = this.policyNumber().trim();
    const claimantName = this.claimantName().trim();
    const incidentDate = this.incidentDate();
    const amount = Number(this.amountClaimed());

    return (
      policyNumber.length > 0 &&
      policyNumber.length <= CLAIM_LIMITS.policyNumber &&
      claimantName.length > 0 &&
      claimantName.length <= CLAIM_LIMITS.name &&
      incidentDate !== '' &&
      incidentDate <= this.today &&
      this.amountClaimed().trim() !== '' &&
      Number.isFinite(amount) &&
      amount > 0
    );
  });

  constructor() {
    // Browser only (avoids calling the API during SSR).
    afterNextRender(() => {
      this.claimService.getHandlers().subscribe({
        next: (handlers) => this.allHandlers.set(handlers),
        error: () => this.handlersError.set(true),
      });
    });
  }

  protected onInput(field: TextField, event: Event): void {
    this.fields[field].set((event.target as HTMLInputElement | HTMLSelectElement).value);
  }

  protected onPrivateChange(event: Event): void {
    this.isPrivate.set((event.target as HTMLInputElement).checked);
  }

  protected submit(): void {
    if (!this.valid() || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.created.set('');

    this.claimService
      .createClaim({
        policyNumber: this.policyNumber().trim(),
        claimantName: this.claimantName().trim(),
        incidentDate: this.incidentDate(),
        amountClaimed: Number(this.amountClaimed()),
        assignedToUserId: this.assignedTo() || null,
        isPrivate: this.isPrivate(),
      })
      .subscribe({
        next: (claim) => {
          this.created.set(`Created claim ${claim.reference}.`);
          Object.values(this.fields).forEach((field) => field.set(''));
          this.isPrivate.set(false);
          this.saving.set(false);
        },
        error: (err: HttpErrorResponse) => {
          // The API returns validation messages as plain-text 400 bodies.
          this.error.set(
            err.status === 400 && typeof err.error === 'string' && err.error
              ? err.error
              : 'Unable to create the claim.',
          );
          this.saving.set(false);
        },
      });
  }
}
