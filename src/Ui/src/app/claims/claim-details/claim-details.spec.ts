import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { UserService } from '../../auth/user.service';
import { DocumentStatus } from '../../document-list/document.model';
import { ClaimDetail, ClaimDocument, ClaimStatus } from '../claim.model';
import { ClaimService } from '../claim.service';
import { ClaimDetails } from './claim-details';

const document: ClaimDocument = {
  id: 'doc-1',
  fileName: 'report.pdf',
  contentType: 'application/pdf',
  fileSize: 2048,
  status: DocumentStatus.Completed,
  uploadedAt: '2026-09-02T10:00:00Z',
  processedAt: '2026-09-02T10:00:02Z',
  summary: null,
};

const detail: ClaimDetail = {
  claim: {
    id: 'claim-1',
    reference: 'CLM-20260901-AAAA1111',
    policyNumber: 'POL-1',
    claimantName: 'Jane Doe',
    incidentDate: '2026-09-01',
    amountClaimed: 1500.5,
    status: ClaimStatus.InReview,
    assignedTo: 'sam',
    createdAt: '2026-09-02T09:00:00Z',
  },
  documents: [document],
};

type Service = Partial<Record<'getClaim' | 'uploadDocument', unknown>>;

async function render(service: Service, settle = true, canWrite = true) {
  await TestBed.configureTestingModule({
    imports: [ClaimDetails],
    providers: [
      provideRouter([]),
      { provide: ClaimService, useValue: service },
      { provide: UserService, useValue: { canWrite: signal(canWrite) } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', 'claim-1']]) } } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(ClaimDetails);
  if (settle) {
    await fixture.whenStable();
  }
  const el = fixture.nativeElement as HTMLElement;

  const chooseFile = (file: File) => {
    const input = el.querySelector<HTMLInputElement>('input[type="file"]')!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  };
  const uploadButton = () =>
    Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find((b) => /Upload/.test(b.textContent ?? ''))!;

  return { fixture, el, chooseFile, uploadButton };
}

describe('ClaimDetails', () => {
  afterEach(() => vi.useRealTimers());

  it('renders the claim and links each document to its page', async () => {
    const { el } = await render({ getClaim: () => of(detail) });

    expect(el.querySelector('h2')?.textContent).toContain('CLM-20260901-AAAA1111');
    expect(el.textContent).toContain('Jane Doe');
    expect(el.textContent).toContain('POL-1');
    expect(el.textContent).toContain('1,500.50');
    expect(el.textContent).toContain('In review');
    expect(el.textContent).toContain('sam');

    const link = el.querySelector<HTMLAnchorElement>('tbody a')!;
    expect(link.textContent?.trim()).toBe('report.pdf');
    expect(link.getAttribute('href')).toBe('/documents/doc-1');
    expect(el.querySelector('tbody')?.textContent).toContain('2.0 KB');
    expect(el.querySelector('tbody')?.textContent).toContain('Complete');
  });

  it('shows Unassigned when nobody is assigned', async () => {
    const unassigned = { ...detail, claim: { ...detail.claim, assignedTo: null } };
    const { el } = await render({ getClaim: () => of(unassigned) });

    expect(el.textContent).toContain('Unassigned');
  });

  it('shows the empty state when the claim has no documents', async () => {
    const { el } = await render({ getClaim: () => of({ ...detail, documents: [] }) });

    expect(el.textContent).toContain('No documents yet.');
    expect(el.querySelector('table')).toBeNull();
  });

  it('shows not found when the API returns 404', async () => {
    const { el } = await render({
      getClaim: () => throwError(() => new HttpErrorResponse({ status: 404 })),
    });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Claim not found.');
  });

  it('shows a generic error when loading fails', async () => {
    const { el } = await render({
      getClaim: () => throwError(() => new HttpErrorResponse({ status: 500 })),
    });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load claim.');
  });

  it('hides the upload control from a read-only user but still lists documents', async () => {
    const { el } = await render({ getClaim: () => of(detail) }, true, false);

    expect(el.querySelector('input[type="file"]')).toBeNull();
    expect(el.querySelector('tbody a')?.textContent?.trim()).toBe('report.pdf');
  });

  it('keeps Upload disabled until a file is chosen', async () => {
    const { uploadButton, chooseFile } = await render({ getClaim: () => of(detail) });
    expect(uploadButton().disabled).toBe(true);

    chooseFile(new File(['x'], 'a.txt'));

    expect(uploadButton().disabled).toBe(false);
  });

  it('uploads the chosen file to the claim, then reloads and clears the picker', async () => {
    const file = new File(['hello'], 'a.txt', { type: 'text/plain' });
    const uploadDocument = vi.fn(() => of({ ...document, id: 'doc-2' }));
    const getClaim = vi.fn(() => of(detail));
    const { fixture, el, chooseFile, uploadButton } = await render({ getClaim, uploadDocument });
    chooseFile(file);

    uploadButton().click();
    await fixture.whenStable();

    expect(uploadDocument).toHaveBeenCalledWith('claim-1', file);
    expect(getClaim).toHaveBeenCalledTimes(2);
    expect(uploadButton().disabled).toBe(true);
    expect(el.querySelector<HTMLInputElement>('input[type="file"]')!.value).toBe('');
  });

  it('shows an error and keeps the file selected when the upload fails', async () => {
    const uploadDocument = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 500 })));
    const getClaim = vi.fn(() => of(detail));
    const { fixture, el, chooseFile, uploadButton } = await render({ getClaim, uploadDocument });
    chooseFile(new File(['x'], 'a.txt'));

    uploadButton().click();
    await fixture.whenStable();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('File upload failed!');
    expect(getClaim).toHaveBeenCalledTimes(1);
    expect(uploadButton().disabled).toBe(false);
  });

  it('keeps refreshing while a document is processing, then stops', async () => {
    vi.useFakeTimers();
    const processing = { ...detail, documents: [{ ...document, status: DocumentStatus.IndexingDocument }] };
    const getClaim = vi.fn().mockReturnValueOnce(of(processing)).mockReturnValue(of(detail));
    const { fixture } = await render({ getClaim }, false);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    expect(getClaim).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(2000);
    expect(getClaim).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(10000);
    expect(getClaim).toHaveBeenCalledTimes(2);
  });

  it('keeps the page and retries when a background refresh fails', async () => {
    vi.useFakeTimers();
    const processing = { ...detail, documents: [{ ...document, status: DocumentStatus.Processing }] };
    const getClaim = vi
      .fn()
      .mockReturnValueOnce(of(processing))
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
      .mockReturnValue(of(detail));
    const { fixture, el } = await render({ getClaim }, false);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);

    await vi.advanceTimersByTimeAsync(2000);
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(el.querySelector('h2')?.textContent).toContain('CLM-20260901-AAAA1111');

    await vi.advanceTimersByTimeAsync(2000);
    expect(getClaim).toHaveBeenCalledTimes(3);
  });
});
