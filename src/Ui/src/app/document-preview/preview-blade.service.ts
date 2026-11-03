import { Injectable, signal } from '@angular/core';
import { ClaimDocument } from '../claims/claim.model';

/** What the blade needs to show a document: the metadata the list and claim endpoints already return. */
export type PreviewTarget = ClaimDocument;

/** Holds the document currently shown in the right-hand preview blade (null when closed). */
@Injectable({ providedIn: 'root' })
export class PreviewBladeService {
  private readonly current = signal<PreviewTarget | null>(null);

  readonly target = this.current.asReadonly();

  open(target: PreviewTarget): void {
    if (this.current()?.id !== target.id) {
      this.current.set(target);
    }
  }

  close(): void {
    this.current.set(null);
  }
}
