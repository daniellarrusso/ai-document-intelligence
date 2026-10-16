import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { UserService } from '../../auth/user.service';
import { DocumentStatus, DocumentSummary } from '../../document-list/document.model';
import { DocumentService } from '../../document-list/document.service';
import { ClaimDetail, ClaimDocument, ClaimStatus, Handler } from '../claim.model';
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
    assignedToUserId: 'sam-id',
    assignedToName: 'Sam Handler',
    isPrivate: false,
    createdAt: '2026-09-02T09:00:00Z',
  },
  documents: [document],
};

const handlers: Handler[] = [
  { id: 'sam-id', displayName: 'Sam Handler' },
  { id: 'olive-id', displayName: 'Olive Other' },
];

type Service = Partial<
  Record<
    'getClaim' | 'uploadDocument' | 'getHandlers' | 'assignClaim' | 'setPrivacy' | 'linkDocument' | 'unlinkDocument',
    unknown
  >
>;

async function render(
  service: Service,
  settle = true,
  canWrite = true,
  isAdmin = false,
  documentApi: Partial<Record<'searchDocuments', unknown>> = {},
) {
  await TestBed.configureTestingModule({
    imports: [ClaimDetails],
    providers: [
      provideRouter([]),
      { provide: ClaimService, useValue: { getHandlers: () => of(handlers), ...service } },
      { provide: DocumentService, useValue: documentApi },
      { provide: UserService, useValue: { canWrite: signal(canWrite), isAdmin: signal(isAdmin) } },
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
    expect(el.textContent).toContain('Sam Handler');

    const link = el.querySelector<HTMLAnchorElement>('tbody a')!;
    expect(link.textContent?.trim()).toBe('report.pdf');
    // The claim id rides along so the document page can lead back to this claim.
    expect(link.getAttribute('href')).toBe('/documents/doc-1?claim=claim-1');
    expect(el.querySelector('tbody')?.textContent).toContain('2.0 KB');
    expect(el.querySelector('tbody')?.textContent).toContain('Complete');
  });

  it('shows Unassigned when nobody is assigned', async () => {
    const unassigned = { ...detail, claim: { ...detail.claim, assignedToUserId: null, assignedToName: null } };
    const { el } = await render({ getClaim: () => of(unassigned) }, true, false);

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

  it('retries a failed refresh after an upload even when no document was processing before', async () => {
    vi.useFakeTimers();
    const uploaded = { ...detail, documents: [document, { ...document, id: 'doc-2', fileName: 'new.pdf' }] };
    const getClaim = vi
      .fn()
      .mockReturnValueOnce(of(detail))
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })))
      .mockReturnValue(of(uploaded));
    const uploadDocument = vi.fn(() => of({ ...document, id: 'doc-2' }));
    const { fixture, el, chooseFile, uploadButton } = await render({ getClaim, uploadDocument }, false);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    chooseFile(new File(['x'], 'a.txt'));

    uploadButton().click();
    await vi.advanceTimersByTimeAsync(0);
    expect(getClaim).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(2000);
    fixture.detectChanges();
    expect(getClaim).toHaveBeenCalledTimes(3);
    expect(el.querySelectorAll('tbody tr').length).toBe(2);

    await vi.advanceTimersByTimeAsync(10000);
    expect(getClaim).toHaveBeenCalledTimes(3);
  });

  describe('assignment', () => {
    const select = (el: HTMLElement) => el.querySelector<HTMLSelectElement>('#claim-assignee')!;
    const reassignButton = (el: HTMLElement) =>
      Array.from(el.querySelectorAll<HTMLButtonElement>('button')).find((b) => /Reassign/.test(b.textContent ?? ''))!;
    const choose = (fixture: { detectChanges: () => void }, el: HTMLElement, value: string) => {
      select(el).value = value;
      select(el).dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };

    it('shows a dropdown of handlers with the current assignee selected', async () => {
      const { el } = await render({ getClaim: () => of(detail) });

      const options = Array.from(select(el).options);
      expect(options.map((o) => o.textContent?.trim())).toEqual(['Sam Handler', 'Olive Other']);
      expect(select(el).value).toBe('sam-id');
      expect(reassignButton(el).disabled).toBe(true);
    });

    it('keeps showing an assignee who is not in the handler list', async () => {
      const getHandlers = () => of([{ id: 'olive-id', displayName: 'Olive Other' }]);
      const { el } = await render({ getClaim: () => of(detail), getHandlers });

      expect(Array.from(select(el).options).map((o) => o.textContent?.trim())).toEqual(['Sam Handler', 'Olive Other']);
      expect(select(el).value).toBe('sam-id');
    });

    it('offers no dropdown to a read-only user, just the assignee name', async () => {
      const getHandlers = vi.fn(() => of(handlers));
      const { el } = await render({ getClaim: () => of(detail), getHandlers }, true, false);

      expect(el.querySelector('#claim-assignee')).toBeNull();
      expect(el.querySelector('#claim-private')).toBeNull();
      expect(el.textContent).toContain('Sam Handler');
      expect(getHandlers).not.toHaveBeenCalled();
    });

    it('enables Reassign only once a different handler is chosen', async () => {
      const { fixture, el } = await render({ getClaim: () => of(detail) });

      choose(fixture, el, 'sam-id');
      expect(reassignButton(el).disabled).toBe(true);

      choose(fixture, el, 'olive-id');
      expect(reassignButton(el).disabled).toBe(false);
    });

    it('lets an admin reassign and then reloads the claim', async () => {
      const assignClaim = vi.fn(() => of(undefined));
      const reassigned = { ...detail, claim: { ...detail.claim, assignedToUserId: 'olive-id', assignedToName: 'Olive Other' } };
      const getClaim = vi.fn().mockReturnValueOnce(of(detail)).mockReturnValue(of(reassigned));
      const { fixture, el } = await render({ getClaim, assignClaim }, true, true, true);
      choose(fixture, el, 'olive-id');

      reassignButton(el).click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(assignClaim).toHaveBeenCalledWith('claim-1', 'olive-id');
      expect(getClaim).toHaveBeenCalledTimes(2);
      expect(select(el).value).toBe('olive-id');
    });

    it('sends a handler back to the claims list after giving the claim away', async () => {
      const assignClaim = vi.fn(() => of(undefined));
      const getClaim = vi.fn(() => of(detail));
      const { fixture, el } = await render({ getClaim, assignClaim }, true, true, false);
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      choose(fixture, el, 'olive-id');

      reassignButton(el).click();
      await fixture.whenStable();

      expect(navigate).toHaveBeenCalledWith('/claims');
      expect(getClaim).toHaveBeenCalledTimes(1);
    });

    it('shows the server message when the assignee is rejected', async () => {
      const assignClaim = vi.fn(() =>
        throwError(() => new HttpErrorResponse({ status: 400, error: 'The assignee must be a handler.' })),
      );
      const { fixture, el } = await render({ getClaim: () => of(detail), assignClaim });
      choose(fixture, el, 'olive-id');

      reassignButton(el).click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(el.querySelector('[role="alert"]')?.textContent).toContain('The assignee must be a handler.');
      expect(reassignButton(el).disabled).toBe(false);
    });

    it('says so when the list of handlers cannot be loaded', async () => {
      const getHandlers = () => throwError(() => new HttpErrorResponse({ status: 500 }));
      const { el } = await render({ getClaim: () => of(detail), getHandlers });

      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load the list of handlers.');
    });
  });

  describe('privacy', () => {
    it('toggles the claim private and reloads', async () => {
      const setPrivacy = vi.fn(() => of(undefined));
      const getClaim = vi.fn(() => of(detail));
      const { fixture, el } = await render({ getClaim, setPrivacy });
      const box = el.querySelector<HTMLInputElement>('#claim-private')!;
      expect(box.checked).toBe(false);

      box.checked = true;
      box.dispatchEvent(new Event('change'));
      await fixture.whenStable();

      expect(setPrivacy).toHaveBeenCalledWith('claim-1', true);
      expect(getClaim).toHaveBeenCalledTimes(2);
    });

    it('shows an error and reloads when the change fails', async () => {
      const setPrivacy = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 500 })));
      const getClaim = vi.fn(() => of(detail));
      const { fixture, el } = await render({ getClaim, setPrivacy });
      const box = el.querySelector<HTMLInputElement>('#claim-private')!;

      box.checked = true;
      box.dispatchEvent(new Event('change'));
      await fixture.whenStable();
      fixture.detectChanges();

      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to change the claim privacy.');
      expect(getClaim).toHaveBeenCalledTimes(2);
    });

    it('tells a read-only user whether the claim is private', async () => {
      const privateDetail = { ...detail, claim: { ...detail.claim, isPrivate: true } };
      const { el } = await render({ getClaim: () => of(privateDetail) }, true, false);

      expect(el.textContent).toContain('Private');
    });
  });

  describe('document links', () => {
    const unmatched: DocumentSummary[] = [
      {
        id: 'doc-9',
        fileName: 'invoice.pdf',
        contentType: 'application/pdf',
        fileSize: 100,
        status: DocumentStatus.Completed,
        uploadedAt: '2026-09-03T10:00:00Z',
        processedAt: null,
        extractedText: null,
        summary: null,
      },
    ];

    const search = async (el: HTMLElement, fixture: { whenStable: () => Promise<unknown> }, text = '') => {
      const input = el.querySelector<HTMLInputElement>('#match-search')!;
      input.value = text;
      input.dispatchEvent(new Event('input'));
      el.querySelector<HTMLFormElement>('details form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };

    it('searches only unmatched documents', async () => {
      const searchDocuments = vi.fn(() => of(unmatched));
      const { el, fixture } = await render({ getClaim: () => of(detail) }, true, true, false, { searchDocuments });

      await search(el, fixture, ' invoice ');

      expect(searchDocuments).toHaveBeenCalledWith({ search: 'invoice', unmatched: true, pageNumber: 1, pageSize: 10 });
      expect(el.querySelector('details')?.textContent).toContain('invoice.pdf');
    });

    it('says when there are no unmatched documents', async () => {
      const { el, fixture } = await render({ getClaim: () => of(detail) }, true, true, false, {
        searchDocuments: () => of([]),
      });

      await search(el, fixture);

      expect(el.querySelector('details')?.textContent).toContain('No unmatched documents found.');
    });

    it('links a found document, reloads the claim and drops it from the results', async () => {
      const linkDocument = vi.fn(() => of(undefined));
      const getClaim = vi.fn(() => of(detail));
      const { el, fixture } = await render({ getClaim, linkDocument }, true, true, false, {
        searchDocuments: () => of(unmatched),
      });
      await search(el, fixture);

      el.querySelector<HTMLButtonElement>('button[aria-label="Link invoice.pdf to this claim"]')!.click();
      await fixture.whenStable();

      expect(linkDocument).toHaveBeenCalledWith('claim-1', 'doc-9');
      expect(getClaim).toHaveBeenCalledTimes(2);
      expect(el.querySelector('details')?.textContent).not.toContain('invoice.pdf');
    });

    it('keeps the result and shows an error when linking fails', async () => {
      const linkDocument = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 404 })));
      const { el, fixture } = await render({ getClaim: () => of(detail), linkDocument }, true, true, false, {
        searchDocuments: () => of(unmatched),
      });
      await search(el, fixture);

      el.querySelector<HTMLButtonElement>('button[aria-label="Link invoice.pdf to this claim"]')!.click();
      await fixture.whenStable();

      expect(el.textContent).toContain('That document or claim is no longer available.');
      expect(el.querySelector('details')?.textContent).toContain('invoice.pdf');
    });

    it('reports a failed search', async () => {
      const { el, fixture } = await render({ getClaim: () => of(detail) }, true, true, false, {
        searchDocuments: () => throwError(() => new HttpErrorResponse({ status: 500 })),
      });

      await search(el, fixture);

      expect(el.textContent).toContain('Unable to search documents.');
    });

    it('unlinks a document and reloads the claim', async () => {
      const unlinkDocument = vi.fn(() => of(undefined));
      const getClaim = vi.fn(() => of(detail));
      const { el, fixture } = await render({ getClaim, unlinkDocument });

      el.querySelector<HTMLButtonElement>('button[aria-label="Unlink report.pdf from this claim"]')!.click();
      await fixture.whenStable();

      expect(unlinkDocument).toHaveBeenCalledWith('claim-1', 'doc-1');
      expect(getClaim).toHaveBeenCalledTimes(2);
    });

    it('shows a permission error when unlinking is forbidden', async () => {
      const unlinkDocument = vi.fn(() => throwError(() => new HttpErrorResponse({ status: 403 })));
      const { el, fixture } = await render({ getClaim: () => of(detail), unlinkDocument });

      el.querySelector<HTMLButtonElement>('button[aria-label="Unlink report.pdf from this claim"]')!.click();
      await fixture.whenStable();

      expect(el.textContent).toContain('You do not have permission to change this claim.');
    });

    it('offers no linking or unlinking to a read-only user', async () => {
      const { el } = await render({ getClaim: () => of(detail) }, true, false);

      expect(el.querySelector('details')).toBeNull();
      expect(el.querySelector('button[aria-label^="Unlink"]')).toBeNull();
    });
  });
});
