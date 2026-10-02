import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { Claim, ClaimStatus } from '../claim.model';
import { ClaimService } from '../claim.service';
import { CreateClaim } from './create-claim';

const created: Claim = {
  id: '1',
  reference: 'CLM-20260901-AAAA1111',
  policyNumber: 'POL-1',
  claimantName: 'Jane Doe',
  incidentDate: '2026-09-01',
  amountClaimed: 100,
  status: ClaimStatus.Open,
  assignedTo: null,
  createdAt: '2026-09-02T10:00:00Z',
};

async function render(createClaim: (...args: unknown[]) => unknown = () => of(created)) {
  await TestBed.configureTestingModule({
    imports: [CreateClaim],
    providers: [{ provide: ClaimService, useValue: { createClaim } }],
  }).compileComponents();

  const fixture = TestBed.createComponent(CreateClaim);
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;

  const type = (selector: string, value: string) => {
    const input = el.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const submitButton = () => el.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  const fillValid = () => {
    type('#claim-policy', '  POL-1 ');
    type('#claim-claimant', 'Jane Doe');
    type('#claim-date', '2026-09-01');
    type('#claim-amount', '100.5');
  };
  const submit = async () => {
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  };

  return { fixture, el, type, submitButton, fillValid, submit };
}

describe('CreateClaim', () => {
  it('disables submit until the required fields are valid', async () => {
    const { submitButton, type, fillValid } = await render();
    expect(submitButton().disabled).toBe(true);

    fillValid();
    expect(submitButton().disabled).toBe(false);

    type('#claim-policy', '   ');
    expect(submitButton().disabled).toBe(true);
  });

  it('rejects a non-positive amount and a future incident date', async () => {
    const { submitButton, type, fillValid } = await render();
    fillValid();

    type('#claim-amount', '0');
    expect(submitButton().disabled).toBe(true);

    type('#claim-amount', '10');
    type('#claim-date', '2999-01-01');
    expect(submitButton().disabled).toBe(true);
  });

  it('submits trimmed values, with a blank assignee sent as null', async () => {
    const createClaim = vi.fn(() => of(created));
    const { fillValid, submit, type } = await render(createClaim);
    fillValid();
    type('#claim-assignee', '   ');

    await submit();

    expect(createClaim).toHaveBeenCalledWith({
      policyNumber: 'POL-1',
      claimantName: 'Jane Doe',
      incidentDate: '2026-09-01',
      amountClaimed: 100.5,
      assignedTo: null,
    });
  });

  it('confirms the new reference and clears the form on success', async () => {
    const { el, fillValid, submit, submitButton } = await render();
    fillValid();

    await submit();

    expect(el.querySelector('[role="status"]')?.textContent).toContain('CLM-20260901-AAAA1111');
    expect(el.querySelector<HTMLInputElement>('#claim-policy')!.value).toBe('');
    expect(submitButton().disabled).toBe(true);
  });

  it('shows the API validation message when the server rejects the claim', async () => {
    const { el, fillValid, submit } = await render(() =>
      throwError(() => new HttpErrorResponse({ status: 400, error: 'Incident date cannot be in the future.' })),
    );
    fillValid();

    await submit();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Incident date cannot be in the future.');
    expect(el.querySelector<HTMLInputElement>('#claim-policy')!.value).toBe('  POL-1 ');
  });

  it('shows a generic error for other failures', async () => {
    const { el, fillValid, submit } = await render(() =>
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    fillValid();

    await submit();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to create the claim.');
  });

  it('does not submit when the form is invalid', async () => {
    const createClaim = vi.fn(() => of(created));
    const { submit } = await render(createClaim);

    await submit();

    expect(createClaim).not.toHaveBeenCalled();
  });
});
