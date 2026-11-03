import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { DocumentService } from '../document-list/document.service';
import { DocumentPreview, previewKind } from './document-preview';

async function setup(contentType: string, getContent: () => unknown, download: () => unknown = () => of(new Blob())) {
  URL.createObjectURL = vi.fn(() => 'blob:test');
  URL.revokeObjectURL = vi.fn();
  await TestBed.configureTestingModule({
    imports: [DocumentPreview],
    providers: [{ provide: DocumentService, useValue: { getContent, download } }],
  }).compileComponents();

  const fixture = TestBed.createComponent(DocumentPreview);
  fixture.componentRef.setInput('documentId', 'abc');
  fixture.componentRef.setInput('contentType', contentType);
  fixture.componentRef.setInput('fileName', 'file');
  await fixture.whenStable();
  return fixture;
}

describe('previewKind', () => {
  it.each([
    ['application/pdf', 'pdf'],
    ['image/png', 'image'],
    ['IMAGE/JPEG', 'image'],
    ['video/mp4', 'video'],
    ['text/plain', 'text'],
    ['application/zip', 'other'],
    ['application/octet-stream', 'other'],
  ])('maps %s to %s', (type, kind) => {
    expect(previewKind(type)).toBe(kind);
  });
});

describe('DocumentPreview', () => {
  it('shows an image from the fetched content', async () => {
    const fixture = await setup('image/png', () => of(new Blob(['x'], { type: 'image/png' })));

    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    expect(img.getAttribute('src')).toBe('blob:test');
  });

  it('shows a video player for video content', async () => {
    const fixture = await setup('video/mp4', () => of(new Blob(['x'], { type: 'video/mp4' })));

    expect(fixture.nativeElement.querySelector('video')).not.toBeNull();
  });

  it('shows a frame for a PDF', async () => {
    const fixture = await setup('application/pdf', () => of(new Blob(['x'])));

    expect(fixture.nativeElement.querySelector('iframe')).not.toBeNull();
  });

  it('shows plain text content', async () => {
    const fixture = await setup('text/plain', () => of(new Blob(['hello text'])));
    await vi.waitFor(() => expect(fixture.nativeElement.querySelector('pre')?.textContent).toBe('hello text'));
  });

  it('does not fetch content for types it cannot render and offers download instead', async () => {
    const getContent = vi.fn();
    const fixture = await setup('application/zip', getContent);

    expect(getContent).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('No preview is available');
    expect(fixture.nativeElement.querySelector('button')).not.toBeNull();
  });

  it('reports a missing file', async () => {
    const fixture = await setup('image/png', () => throwError(() => ({ status: 404 })));

    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('no longer available');
  });

  it('reports a failed preview', async () => {
    const fixture = await setup('image/png', () => throwError(() => ({ status: 500 })));

    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Unable to load a preview');
  });

  it('releases the object URL when destroyed', async () => {
    const fixture = await setup('image/png', () => of(new Blob(['x'])));

    fixture.destroy();

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test');
  });
});
