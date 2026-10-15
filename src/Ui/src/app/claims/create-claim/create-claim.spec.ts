import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { UserService } from '../../auth/user.service';
import { Claim, ClaimStatus, Handler } from '../claim.model';
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
  assignedToUserId: null,
  assignedToName: null,
  isPrivate: false,
  createdAt: '2026-09-02T10:00:00Z',
};

const handlers: Handler[] = [
  { id: 'me-id', displayName: 'Me Myself' },
  { id: 'sam-id', displayName: 'Sam Handler' },
  { id: 'olive-id', displayName: 'Olive Other' },
];

async function render(
  createClaim: (...args: unknown[]) => unknown = () => of(created),
  getHandlers: () => unknown = () => of(handlers),
) {
  await TestBed.configureTestingModule({
    imports: [CreateClaim],
    providers: [
      { provide: ClaimService, useValue: { createClaim, getHandlers } },
      { provide: UserService, useValue: { user: signal({ id: 'me-id', name: 'Me', roles: ['Handler'] }) } },
    ],
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
  const choose = (selector: string, value: string) => {
    const select = el.querySelector<HTMLSelectElement>(selector)!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
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

  return { fixture, el, type, choose, submitButton, fillValid, submit };
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

  it('submits trimmed values, defaulting to the creator and a public claim', async () => {
    const createClaim = vi.fn(() => of(created));
    const { fillValid, submit } = await render(createClaim);
    fillValid();

    await submit();

    expect(createClaim).toHaveBeenCalledWith({
      policyNumber: 'POL-1',
      claimantName: 'Jane Doe',
      incidentDate: '2026-09-01',
      amountClaimed: 100.5,
      assignedToUserId: null,
      isPrivate: false,
    });
  });

  it('offers a dropdown of the other handlers, with the creator as the default option', async () => {
    const { el } = await render();

    const options = Array.from(el.querySelectorAll<HTMLOptionElement>('#claim-assignee option'));
    expect(el.querySelector('#claim-assignee')?.tagName).toBe('SELECT');
    expect(options.map((o) => o.textContent?.trim())).toEqual(['Me (default)', 'Sam Handler', 'Olive Other']);
    expect(options[0].value).toBe('');
  });

  it('assigns to the chosen handler and can mark the claim private', async () => {
    const createClaim = vi.fn(() => of(created));
    const { el, fixture, fillValid, submit, choose } = await render(createClaim);
    fillValid();
    choose('#claim-assignee', 'sam-id');
    const privateBox = el.querySelector<HTMLInputElement>('#claim-private')!;
    privateBox.checked = true;
    privateBox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    await submit();

    expect(createClaim).toHaveBeenCalledWith(
      expect.objectContaining({ assignedToUserId: 'sam-id', isPrivate: true }),
    );
  });

  it('resets the assignee and privacy after a successful create', async () => {
    const { el, fixture, fillValid, submit, choose } = await render();
    fillValid();
    choose('#claim-assignee', 'olive-id');
    const privateBox = el.querySelector<HTMLInputElement>('#claim-private')!;
    privateBox.checked = true;
    privateBox.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    await submit();
    fixture.detectChanges();

    expect(el.querySelector<HTMLSelectElement>('#claim-assignee')!.value).toBe('');
    expect(el.querySelector<HTMLInputElement>('#claim-private')!.checked).toBe(false);
  });

  it('still lets the creator file a claim when the handler list fails to load', async () => {
    const createClaim = vi.fn(() => of(created));
    const { el, fillValid, submit } = await render(createClaim, () => throwError(() => new HttpErrorResponse({ status: 500 })));
    fillValid();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load the list of handlers.');
    await submit();

    expect(createClaim).toHaveBeenCalled();
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
