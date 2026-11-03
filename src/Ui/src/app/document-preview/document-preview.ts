import { Component, DestroyRef, afterNextRender, inject, input, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { DocumentService } from '../document-list/document.service';

export type PreviewKind = 'pdf' | 'image' | 'video' | 'text' | 'other';

// Plain text is shown as text, but only the start of a large file.
const MAX_TEXT_CHARS = 100_000;

/** Picks the viewer for a stored content type; anything the browser can't render in place falls back to download. */
export function previewKind(contentType: string): PreviewKind {
  const type = contentType.toLowerCase();
  if (type === 'application/pdf') {
    return 'pdf';
  }
  if (type.startsWith('image/')) {
    return 'image';
  }
  if (type.startsWith('video/')) {
    return 'video';
  }
  if (type === 'text/plain') {
    return 'text';
  }
  return 'other';
}

@Component({
  selector: 'app-document-preview',
  templateUrl: './document-preview.html',
})
export class DocumentPreview {
  private readonly documentService = inject(DocumentService);
  private readonly sanitizer = inject(DomSanitizer);

  readonly documentId = input.required<string>();
  readonly contentType = input.required<string>();
  readonly fileName = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly downloading = signal(false);
  protected readonly kind = signal<PreviewKind>('other');
  protected readonly objectUrl = signal<string | null>(null);
  protected readonly text = signal('');
  protected readonly frameUrl = signal<SafeResourceUrl | null>(null);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.revoke());

    // Browser only: object URLs don't exist during SSR.
    afterNextRender(() => this.load());
  }

  private load(): void {
    const kind = previewKind(this.contentType());
    this.kind.set(kind);

    if (kind === 'other') {
      this.loading.set(false);
      return;
    }

    // Fetched through HttpClient so the auth interceptor adds the bearer token (<img>/<video>/<iframe> can't).
    this.documentService.getContent(this.documentId()).subscribe({
      next: (blob) => {
        if (kind === 'text') {
          void blob
            .slice(0, MAX_TEXT_CHARS)
            .text()
            .then((text) => {
              this.text.set(text);
              this.loading.set(false);
            });
          return;
        }

        const url = URL.createObjectURL(blob);
        this.objectUrl.set(url);
        this.frameUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
        this.loading.set(false);
      },
      error: (err: { status?: number }) => {
        this.error.set(err.status === 404 ? 'This file is no longer available.' : 'Unable to load a preview.');
        this.loading.set(false);
      },
    });
  }

  protected download(): void {
    if (this.downloading()) {
      return;
    }

    this.downloading.set(true);
    this.documentService.download(this.documentId()).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = this.fileName();
        link.click();
        URL.revokeObjectURL(url);
        this.downloading.set(false);
      },
      error: () => {
        this.error.set('Unable to download the file.');
        this.downloading.set(false);
      },
    });
  }

  private revoke(): void {
    const url = this.objectUrl();
    if (url) {
      URL.revokeObjectURL(url);
    }
  }
}
