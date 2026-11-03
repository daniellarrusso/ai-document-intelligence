import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { DocumentStatus } from '../document-list/document.model';
import { DocumentService } from '../document-list/document.service';
import { PreviewBlade } from './preview-blade';
import { PreviewBladeService, PreviewTarget } from './preview-blade.service';
import { PreviewBladeTrigger } from './preview-blade-trigger';

const target = (over: Partial<PreviewTarget> = {}): PreviewTarget => ({
  id: 'abc',
  fileName: 'photo.png',
  contentType: 'image/png',
  fileSize: 2048,
  status: DocumentStatus.Completed,
  uploadedAt: '2026-01-02T03:04:05Z',
  processedAt: null,
  summary: 'A photo of damage.',
  ...over,
});

@Component({
  imports: [PreviewBladeTrigger],
  template: `<a [appPreviewBlade]="doc">name</a>`,
})
class Host {
  doc = target();
}

async function setup() {
  vi.useFakeTimers();
  URL.createObjectURL = vi.fn(() => 'blob:x');
  URL.revokeObjectURL = vi.fn();
  const getContent = vi.fn(() => of(new Blob(['x'])));
  await TestBed.configureTestingModule({
    imports: [Host, PreviewBlade],
    providers: [provideRouter([]), { provide: DocumentService, useValue: { getContent, download: vi.fn() } }],
  }).compileComponents();
  return { blade: TestBed.inject(PreviewBladeService), getContent };
}

describe('PreviewBladeTrigger', () => {
  afterEach(() => vi.useRealTimers());

  it('opens the blade after a short hover', async () => {
    const { blade } = await setup();
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const link = fixture.nativeElement.querySelector('a') as HTMLElement;

    link.dispatchEvent(new Event('mouseenter'));
    expect(blade.target()).toBeNull();
    vi.advanceTimersByTime(350);

    expect(blade.target()?.id).toBe('abc');
  });

  it('does not open if the pointer leaves first, and stays open once opened', async () => {
    const { blade } = await setup();
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const link = fixture.nativeElement.querySelector('a') as HTMLElement;

    link.dispatchEvent(new Event('mouseenter'));
    link.dispatchEvent(new Event('mouseleave'));
    vi.advanceTimersByTime(350);
    expect(blade.target()).toBeNull();

    link.dispatchEvent(new Event('mouseenter'));
    vi.advanceTimersByTime(350);
    link.dispatchEvent(new Event('mouseleave'));
    expect(blade.target()?.id).toBe('abc');
  });
});

describe('PreviewBlade', () => {
  afterEach(() => vi.useRealTimers());

  it('renders nothing while closed', async () => {
    await setup();
    const fixture = TestBed.createComponent(PreviewBlade);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('aside')).toBeNull();
  });

  it('shows the preview and metadata for any document type', async () => {
    const { blade, getContent } = await setup();
    const fixture = TestBed.createComponent(PreviewBlade);
    blade.open(target());
    fixture.detectChanges();
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('photo.png');
    expect(text).toContain('image/png');
    expect(text).toContain('2.0 KB');
    expect(text).toContain('Complete');
    expect(text).toContain('A photo of damage.');
    expect(getContent).toHaveBeenCalledWith('abc');
    expect(fixture.nativeElement.querySelector('img')).not.toBeNull();
  });

  it('swaps to another document and loads its content', async () => {
    const { blade, getContent } = await setup();
    const fixture = TestBed.createComponent(PreviewBlade);
    blade.open(target());
    fixture.detectChanges();
    await fixture.whenStable();

    blade.open(target({ id: 'def', fileName: 'clip.mp4', contentType: 'video/mp4' }));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(getContent).toHaveBeenCalledWith('def');
    expect(fixture.nativeElement.querySelector('video')).not.toBeNull();
  });

  it('closes with the close button and with Escape', async () => {
    const { blade } = await setup();
    const fixture = TestBed.createComponent(PreviewBlade);
    blade.open(target());
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button[aria-label="Close preview"]') as HTMLButtonElement).click();
    expect(blade.target()).toBeNull();

    blade.open(target());
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(blade.target()).toBeNull();
  });
});
