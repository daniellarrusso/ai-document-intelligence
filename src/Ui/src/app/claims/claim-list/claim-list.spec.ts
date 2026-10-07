import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { Claim, ClaimStatus } from '../claim.model';
import { ClaimService } from '../claim.service';
import { ClaimList } from './claim-list';

const sample: Claim = {
  id: '1',
  reference: 'CLM-20260901-AAAA1111',
  policyNumber: 'POL-1',
  claimantName: 'Jane Doe',
  incidentDate: '2026-09-01',
  amountClaimed: 1500.5,
  status: ClaimStatus.InReview,
  assignedTo: null,
  createdAt: '2026-09-02T10:00:00Z',
};

async function render(getClaims: (...args: unknown[]) => unknown, created = signal(0), settle = true) {
  await TestBed.configureTestingModule({
    imports: [ClaimList],
    providers: [provideRouter([]), { provide: ClaimService, useValue: { getClaims, created } }],
  }).compileComponents();

  const fixture = TestBed.createComponent(ClaimList);
  if (settle) {
    await fixture.whenStable();
  }
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

function type(el: HTMLElement, selector: string, value: string): void {
  const input = el.querySelector<HTMLInputElement>(selector)!;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('ClaimList', () => {
  afterEach(() => vi.useRealTimers());

  it('renders a row per claim', async () => {
    const { el } = await render(() => of([sample]));

    const cells = Array.from(el.querySelectorAll('tbody td')).map((c) => c.textContent?.trim());
    expect(cells.slice(0, 7)).toEqual([
      'CLM-20260901-AAAA1111',
      'Jane Doe',
      'POL-1',
      'Sep 1, 2026',
      '1,500.50',
      'In review',
      'Unassigned',
    ]);
  });

  it('links the reference to the claim page', async () => {
    const { el } = await render(() => of([sample]));

    const link = el.querySelector<HTMLAnchorElement>('tbody a')!;
    expect(link.textContent?.trim()).toBe('CLM-20260901-AAAA1111');
    expect(link.getAttribute('href')).toBe('/claims/1');
  });

  it('shows the empty state when there are no claims', async () => {
    const { el } = await render(() => of([]));

    expect(el.textContent).toContain('No claims yet.');
    expect(el.querySelector('table')).toBeNull();
  });

  it('shows an error when loading fails', async () => {
    const { el } = await render(() => throwError(() => new Error('boom')));

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load claims.');
  });

  it('reloads after a claim is created', async () => {
    const getClaims = vi.fn(() => of([sample]));
    const created = signal(0);
    const { fixture } = await render(getClaims, created);

    created.update((n) => n + 1);
    await fixture.whenStable();

    expect(getClaims).toHaveBeenCalledTimes(2);
  });

  it('searches after a pause in typing, from the first page', async () => {
    vi.useFakeTimers();
    const getClaims = vi.fn(() => of([sample]));
    const { fixture, el } = await render(getClaims, signal(0), false);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);

    type(el, '#claim-search', 'smi');
    type(el, '#claim-search', 'smith');
    expect(getClaims).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(300);

    expect(getClaims).toHaveBeenCalledTimes(2);
    expect(getClaims).toHaveBeenLastCalledWith({ search: 'smith', status: null, pageNumber: 1, pageSize: 10 });
  });

  it('shows a no-match message when a search finds nothing', async () => {
    vi.useFakeTimers();
    const getClaims = vi.fn().mockReturnValueOnce(of([sample])).mockReturnValue(of([]));
    const { fixture, el } = await render(getClaims, signal(0), false);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);

    type(el, '#claim-search', 'zzz');
    await vi.advanceTimersByTimeAsync(300);
    fixture.detectChanges();

    expect(el.textContent).toContain('No claims match your search.');
  });

  it('filters by status', async () => {
    const getClaims = vi.fn(() => of([sample]));
    const { el } = await render(getClaims);

    const select = el.querySelector<HTMLSelectElement>('#claim-status')!;
    select.value = String(ClaimStatus.Approved);
    select.dispatchEvent(new Event('change'));

    expect(getClaims).toHaveBeenLastCalledWith({ search: '', status: ClaimStatus.Approved, pageNumber: 1, pageSize: 10 });
  });

  it('pages forward and back when a full page is returned', async () => {
    const fullPage = Array.from({ length: 10 }, (_, i) => ({ ...sample, id: String(i) }));
    const getClaims = vi.fn(() => of(fullPage));
    const { fixture, el } = await render(getClaims);
    const buttons = () => Array.from(el.querySelectorAll<HTMLButtonElement>('button'));
    const button = (label: string) => buttons().find((b) => b.textContent?.trim() === label)!;

    expect(button('Previous').disabled).toBe(true);
    button('Next').click();
    fixture.detectChanges();

    expect(getClaims).toHaveBeenLastCalledWith({ search: '', status: null, pageNumber: 2, pageSize: 10 });
    expect(el.textContent).toContain('Page 2');

    button('Previous').click();
    expect(getClaims).toHaveBeenLastCalledWith({ search: '', status: null, pageNumber: 1, pageSize: 10 });
  });

  it('hides paging when everything fits on one page', async () => {
    const { el } = await render(() => of([sample]));

    expect(el.textContent).not.toContain('Next');
  });
});
