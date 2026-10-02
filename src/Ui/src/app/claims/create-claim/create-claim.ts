import { HttpErrorResponse } from '@angular/common/http';
import { Component, WritableSignal, computed, inject, signal } from '@angular/core';
import { CLAIM_LIMITS } from '../claim.model';
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

  protected readonly limits = CLAIM_LIMITS;
  protected readonly today = todayIso();

  protected readonly policyNumber = signal('');
  protected readonly claimantName = signal('');
  protected readonly incidentDate = signal('');
  protected readonly amountClaimed = signal('');
  protected readonly assignedTo = signal('');

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
      this.assignedTo().trim().length <= CLAIM_LIMITS.name &&
      incidentDate !== '' &&
      incidentDate <= this.today &&
      this.amountClaimed().trim() !== '' &&
      Number.isFinite(amount) &&
      amount > 0
    );
  });

  protected onInput(field: TextField, event: Event): void {
    this.fields[field].set((event.target as HTMLInputElement).value);
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
        assignedTo: this.assignedTo().trim() || null,
      })
      .subscribe({
        next: (claim) => {
          this.created.set(`Created claim ${claim.reference}.`);
          Object.values(this.fields).forEach((field) => field.set(''));
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
