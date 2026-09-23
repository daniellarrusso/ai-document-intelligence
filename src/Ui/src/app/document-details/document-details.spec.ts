import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
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
};

async function render(service: Partial<Record<'getDocument' | 'delete', unknown>>) {
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
  const click = async (selector: string, text?: string) => {
    const buttons = Array.from(el.querySelectorAll<HTMLButtonElement>(selector));
    buttons.find((b) => !text || b.textContent?.trim() === text)!.click();
    await fixture.whenStable();
  };
  return { fixture, el, click };
}

describe('DocumentDetails', () => {
  it('renders the document details', async () => {
    const { el } = await render({ getDocument: () => of(sample) });

    expect(el.querySelector('h2')?.textContent).toContain('report.pdf');
    expect(el.textContent).toContain('Completed');
    expect(el.textContent).toContain('2.0 KB');
    expect(el.textContent).toContain('application/pdf');
  });

  it('shows not found when the API returns 404', async () => {
    const { el } = await render({ getDocument: () => throwError(() => ({ status: 404 })) });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Document not found.');
  });

  it('shows a generic error when loading fails', async () => {
    const { el } = await render({ getDocument: () => throwError(() => ({ status: 500 })) });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load document.');
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
});
