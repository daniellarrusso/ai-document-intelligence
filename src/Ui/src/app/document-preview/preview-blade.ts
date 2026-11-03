import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { documentStatusLabel } from '../document-list/document.model';
import { formatFileSize } from '../shared/format-file-size';
import { DocumentPreview } from './document-preview';
import { PreviewBladeService } from './preview-blade.service';

@Component({
  selector: 'app-preview-blade',
  imports: [DatePipe, RouterLink, DocumentPreview],
  templateUrl: './preview-blade.html',
  host: { '(document:keydown.escape)': 'blade.close()' },
})
export class PreviewBlade {
  protected readonly blade = inject(PreviewBladeService);

  protected readonly statusLabel = documentStatusLabel;
  protected readonly formatSize = formatFileSize;

  constructor() {
    // The blade belongs to the page it was opened on.
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationStart),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.blade.close());
  }
}
