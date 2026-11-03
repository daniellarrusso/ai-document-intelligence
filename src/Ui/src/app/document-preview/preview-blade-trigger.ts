import { DestroyRef, Directive, inject, input } from '@angular/core';
import { PreviewBladeService, PreviewTarget } from './preview-blade.service';

const HOVER_DELAY_MS = 300;

/**
 * Opens the preview blade for a document while its name is hovered or focused. The blade stays open after the
 * pointer leaves (so it can be scrolled, played and read) until it is closed; hovering another document swaps it.
 */
@Directive({
  selector: '[appPreviewBlade]',
  host: {
    '(mouseenter)': 'schedule()',
    '(focus)': 'schedule()',
    '(mouseleave)': 'cancel()',
    '(blur)': 'cancel()',
  },
})
export class PreviewBladeTrigger {
  private readonly blade = inject(PreviewBladeService);

  readonly target = input.required<PreviewTarget>({ alias: 'appPreviewBlade' });

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.cancel());
  }

  protected schedule(): void {
    this.cancel();
    this.timer = setTimeout(() => this.blade.open(this.target()), HOVER_DELAY_MS);
  }

  protected cancel(): void {
    clearTimeout(this.timer);
    this.timer = undefined;
  }
}
