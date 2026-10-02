import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { NEVER, of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { DocumentStatus, DocumentSummary } from '../document-list/document.model';
import { DocumentService } from '../document-list/document.service';
import { DocumentDetails } from './document-details';

const sample: DocumentSummary = {
  id: 'abc',
  fileName: 'report.pdf',
  contentType: 'application/pdf',
  fileSize: 2048,
  status: DocumentStatus.Completed,
  uploadedAt: '2026-01-01T10:00:00Z',
  processedAt: '2026-01-01T10:00:02Z',
  extractedText: 'Hello World',
  summary: 'A brief report about hello world.',
};

async function render(service: Partial<Record<'getDocument' | 'delete' | 'ask', unknown>>) {
  await TestBed.configureTestingModule({
    imports: [DocumentDetails],
    providers: [
      provideRouter([]),
      { provide: DocumentService, useValue: service },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', 'abc']]) } } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(DocumentDetails);
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  const type = async (value: string) => {
    const textarea = el.querySelector<HTMLTextAreaElement>('textarea')!;
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const click = async (selector: string, text?: string) => {
    const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>(selector));
    buttons.find((b) => !text || b.textContent?.trim() === text)!.click();
    await fixture.whenStable();
  };
  return { fixture, el, click, type };
}

describe('DocumentDetails', () => {
  it('renders the document details', async () => {
    const { el } = await render({ getDocument: () => of(sample) });

    expect(el.querySelector('h2')?.textContent).toContain('report.pdf');
    expect(el.textContent).toContain('Complete');
    expect(el.textContent).toContain('2.0 KB');
    expect(el.textContent).toContain('application/pdf');
  });

  it('renders the extracted text when present', async () => {
    const { el } = await render({ getDocument: () => of(sample) });

    expect(el.querySelector('pre')?.textContent).toContain('Hello World');
  });

  it('shows a processing message while extraction is in progress', async () => {
    const processing = { ...sample, status: DocumentStatus.Processing, extractedText: null, summary: null };
    const { el } = await render({ getDocument: () => of(processing) });

    expect(el.querySelector('pre')).toBeNull();
    expect(el.textContent).toContain('Still processing...');
  });

  it('shows the progress stepper with the current step highlighted', async () => {
    const extracting = { ...sample, status: DocumentStatus.ExtractingText, extractedText: null, summary: null };
    const { el } = await render({ getDocument: () => of(extracting) });

    const steps = Array.from(el.querySelectorAll('[role="status"] li')).map((li) => li.textContent?.trim());
    expect(steps).toEqual([
      '✓Processing document...',
      '●Extracting text...',
      '○Generating summary...',
      '○Indexing for search...',
      '○Complete',
    ]);
  });

  it('hides the progress stepper once the document is completed', async () => {
    const { el } = await render({ getDocument: () => of(sample) });

    expect(el.querySelector('[role="status"]')).toBeNull();
  });

  it('hides the progress stepper when the document failed', async () => {
    const failed = { ...sample, status: DocumentStatus.Failed, extractedText: null, summary: null };
    const { el } = await render({ getDocument: () => of(failed) });

    expect(el.querySelector('[role="status"]')).toBeNull();
  });

  it('keeps polling while in progress, then stops once completed', async () => {
    vi.useFakeTimers();
    try {
      const extracting = { ...sample, status: DocumentStatus.ExtractingText, extractedText: null, summary: null };
      const getDocument = vi
        .fn()
        .mockReturnValueOnce(of(extracting))
        .mockReturnValue(of(sample));
      await TestBed.configureTestingModule({
        imports: [DocumentDetails],
        providers: [
          provideRouter([]),
          { provide: DocumentService, useValue: { getDocument } },
          { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['id', 'abc']]) } } },
        ],
      }).compileComponents();
      const fixture = TestBed.createComponent(DocumentDetails);
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(0);
      expect(getDocument).toHaveBeenCalledTimes(1);

      await vi.advanceTimersByTimeAsync(2000);
      expect(getDocument).toHaveBeenCalledTimes(2);

      await vi.advanceTimersByTimeAsync(10000);
      expect(getDocument).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows a failure message when extraction failed', async () => {
    const failed = { ...sample, status: DocumentStatus.Failed, extractedText: null, summary: null };
    const { el } = await render({ getDocument: () => of(failed) });

    expect(el.querySelector('pre')).toBeNull();
    expect(el.textContent).toContain('Text extraction failed for this document.');
  });

  it('renders the summary when present', async () => {
    const { el } = await render({ getDocument: () => of(sample) });

    expect(el.textContent).toContain('A brief report about hello world.');
  });

  it('shows summary unavailable when completed without a summary', async () => {
    const noSummary = { ...sample, summary: null };
    const { el } = await render({ getDocument: () => of(noSummary) });

    expect(el.textContent).toContain('Summary unavailable.');
  });

  it('shows not found when the API returns 404', async () => {
    const { el } = await render({ getDocument: () => throwError(() => ({ status: 404 })) });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Document not found.');
  });

  it('shows a generic error when loading fails', async () => {
    const { el } = await render({ getDocument: () => throwError(() => ({ status: 500 })) });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load document.');
  });

  it('links back to the documents list for a standalone document', async () => {
    const { el } = await render({ getDocument: () => of(sample) });

    const back = el.querySelector<HTMLAnchorElement>('a')!;
    expect(back.textContent).toContain('Back to documents');
    expect(back.getAttribute('href')).toBe('/');
  });

  it('links back to the claim when the document belongs to one', async () => {
    const { el } = await render({ getDocument: () => of({ ...sample, claimId: 'claim-1' }) });

    const back = el.querySelector<HTMLAnchorElement>('a')!;
    expect(back.textContent).toContain('Back to claim');
    expect(back.getAttribute('href')).toBe('/claims/claim-1');
  });

  it('returns to the claim after deleting one of its documents', async () => {
    const del = vi.fn(() => of(undefined));
    const { click } = await render({ getDocument: () => of({ ...sample, claimId: 'claim-1' }), delete: del });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    await click('button', 'Delete');
    await click('[role="dialog"] button', 'Delete');

    expect(navigate).toHaveBeenCalledWith('/claims/claim-1');
  });

  it('asks for confirmation before deleting and does nothing on cancel', async () => {
    const del = vi.fn(() => of(undefined));
    const { el, click } = await render({ getDocument: () => of(sample), delete: del });
    expect(el.querySelector('[role="dialog"]')).toBeNull();

    await click('button', 'Delete');
    expect(el.querySelector('[role="dialog"]')).not.toBeNull();

    await click('[role="dialog"] button', 'Cancel');

    expect(el.querySelector('[role="dialog"]')).toBeNull();
    expect(del).not.toHaveBeenCalled();
  });

  it('deletes the document and returns to the list when confirmed', async () => {
    const del = vi.fn(() => of(undefined));
    const { click } = await render({ getDocument: () => of(sample), delete: del });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    await click('button', 'Delete');
    await click('[role="dialog"] button', 'Delete');

    expect(del).toHaveBeenCalledWith('abc');
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('keeps the dialog open and shows an error when the delete fails', async () => {
    const del = vi.fn(() => throwError(() => new Error('boom')));
    const { el, click } = await render({ getDocument: () => of(sample), delete: del });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    await click('button', 'Delete');
    await click('[role="dialog"] button', 'Delete');

    expect(el.querySelector('[role="dialog"] [role="alert"]')?.textContent).toContain('Unable to delete');
    expect(navigate).not.toHaveBeenCalled();
  });

  describe('asking questions', () => {
    const answer = {
      answer: 'Flooding is excluded [1].',
      sources: [
        { chunkIndex: 3, text: 'Damage caused by flooding is not covered.', score: 0.912 },
        { chunkIndex: 7, text: 'Gradual leaks are excluded.', score: 0.8 },
      ],
    };

    it('only offers the question box once the document is completed', async () => {
      const processing = { ...sample, status: DocumentStatus.IndexingDocument };
      const { el } = await render({ getDocument: () => of(processing) });

      expect(el.querySelector('textarea')).toBeNull();
    });

    it('does not offer the question box when the document failed', async () => {
      const failed = { ...sample, status: DocumentStatus.Failed };
      const { el } = await render({ getDocument: () => of(failed) });

      expect(el.querySelector('textarea')).toBeNull();
    });

    it('disables Ask until a question is entered', async () => {
      const { el, type } = await render({ getDocument: () => of(sample), ask: vi.fn() });
      const ask = el.querySelector<HTMLButtonElement>('form button[type="submit"]')!;
      expect(ask.disabled).toBe(true);

      await type('   ');
      expect(ask.disabled).toBe(true);

      await type('What is excluded?');
      expect(ask.disabled).toBe(false);
    });

    it('sends the trimmed question and shows the answer with its sources', async () => {
      const ask = vi.fn(() => of(answer));
      const { el, click, type } = await render({ getDocument: () => of(sample), ask });

      await type('  What are the exclusions for water damage?  ');
      await click('form button[type="submit"]');

      expect(ask).toHaveBeenCalledWith('abc', 'What are the exclusions for water damage?');
      expect(el.textContent).toContain('Flooding is excluded [1].');
      expect(el.querySelector('details summary')?.textContent).toContain('Sources (2)');
      const sources = Array.from(el.querySelectorAll('details li')).map((li) => li.textContent);
      expect(sources[0]).toContain('Relevance 91%');
      expect(sources[0]).toContain('Damage caused by flooding is not covered.');
      expect(sources[1]).toContain('Gradual leaks are excluded.');
    });

    it('shows a searching state and blocks resubmission while waiting', async () => {
      const ask = vi.fn(() => NEVER);
      const { el, click, type } = await render({ getDocument: () => of(sample), ask });

      await type('What is excluded?');
      await click('form button[type="submit"]');

      const button = el.querySelector<HTMLButtonElement>('form button[type="submit"]')!;
      expect(button.textContent?.trim()).toBe('Searching document...');
      expect(button.disabled).toBe(true);
      expect(el.querySelector<HTMLTextAreaElement>('textarea')!.disabled).toBe(true);
    });

    it('explains when the document has no searchable content (409)', async () => {
      const ask = vi.fn(() => throwError(() => ({ status: 409 })));
      const { el, click, type } = await render({ getDocument: () => of(sample), ask });

      await type('What is excluded?');
      await click('form button[type="submit"]');

      expect(el.querySelector('form + [role="alert"]')?.textContent).toContain('no searchable content');
      expect(el.querySelector<HTMLButtonElement>('form button[type="submit"]')!.disabled).toBe(false);
    });

    it('shows a generic error when the request fails and clears it on the next attempt', async () => {
      const ask = vi
        .fn()
        .mockReturnValueOnce(throwError(() => ({ status: 503 })))
        .mockReturnValue(of(answer));
      const { el, click, type } = await render({ getDocument: () => of(sample), ask });

      await type('What is excluded?');
      await click('form button[type="submit"]');
      expect(el.querySelector('form + [role="alert"]')?.textContent).toContain('Unable to get an answer');
      expect(el.textContent).not.toContain('Flooding is excluded');

      await click('form button[type="submit"]');
      expect(el.querySelector('form + [role="alert"]')).toBeNull();
      expect(el.textContent).toContain('Flooding is excluded [1].');
    });
  });
});
