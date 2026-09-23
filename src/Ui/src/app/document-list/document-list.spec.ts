import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { DocumentList } from './document-list';
import { DocumentStatus, DocumentSummary } from './document.model';
import { DocumentService } from './document.service';

const sample: DocumentSummary = {
  id: '1',
  fileName: 'report.pdf',
  contentType: 'application/pdf',
  fileSize: 2048,
  status: DocumentStatus.Completed,
  uploadedAt: '2026-01-01T10:00:00Z',
  processedAt: null,
};

async function render(getDocuments: () => unknown, uploads = signal(0), settle = true) {
  await TestBed.configureTestingModule({
    imports: [DocumentList],
    providers: [provideRouter([]), { provide: DocumentService, useValue: { getDocuments, uploads } }],
  }).compileComponents();

  const fixture = TestBed.createComponent(DocumentList);
  if (settle) {
    await fixture.whenStable();
  }
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('DocumentList', () => {
  it('renders a row per document', async () => {
    const { el } = await render(() => of([sample]));

    const cells = Array.from(el.querySelectorAll('tbody td')).map((c) => c.textContent?.trim());
    expect(cells.slice(0, 3)).toEqual(['report.pdf', '2.0 KB', 'Completed']);
  });

  it('shows the empty state when there are no documents', async () => {
    const { el } = await render(() => of([]));

    expect(el.textContent).toContain('No documents uploaded yet.');
    expect(el.querySelector('table')).toBeNull();
  });

  it('shows an error when loading fails', async () => {
    const { el } = await render(() => throwError(() => new Error('boom')));

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load documents.');
  });

  it('reloads when Refresh is clicked', async () => {
    const getDocuments = vi.fn(() => of([sample]));
    const { el } = await render(getDocuments);

    el.querySelector<HTMLButtonElement>('button')!.click();

    expect(getDocuments).toHaveBeenCalledTimes(2);
  });

  it('reloads after a document is uploaded', async () => {
    const getDocuments = vi.fn(() => of([sample]));
    const uploads = signal(0);
    const { fixture } = await render(getDocuments, uploads);

    uploads.update((n) => n + 1);
    await fixture.whenStable();

    expect(getDocuments).toHaveBeenCalledTimes(2);
  });

  it('keeps refreshing while a document is processing, then stops', async () => {
    vi.useFakeTimers();
    try {
      const processing = { ...sample, status: DocumentStatus.Processing };
      const getDocuments = vi
        .fn()
        .mockReturnValueOnce(of([processing]))
        .mockReturnValue(of([sample]));
      const { fixture } = await render(getDocuments, signal(0), false);
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(0);
      expect(getDocuments).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(2000);
      expect(getDocuments).toHaveBeenCalledTimes(2);

      await vi.advanceTimersByTimeAsync(10000);
      expect(getDocuments).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });
});
