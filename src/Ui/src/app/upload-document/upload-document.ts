import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DocumentService } from '../document-list/document.service';

@Component({
  selector: 'app-upload-document',
  templateUrl: './upload-document.html',
  styleUrl: './upload-document.css',
})
export class UploadDocument {
  private readonly documentService = inject(DocumentService);
  private readonly router = inject(Router);

  protected readonly title = 'Upload Document';

  readonly selectedFile = signal<File | null>(null);
  readonly uploading = signal(false);
  readonly message = signal('');

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile.set(input.files?.[0] ?? null);
  }

  upload(): void {
    const file = this.selectedFile();
    if (!file) {
      return;
    }

    this.uploading.set(true);
    this.message.set('');

    // Navigate to the details page on success: that's where processing progress is now shown.
    this.documentService.upload(file).subscribe({
      next: (document) => void this.router.navigateByUrl(`/documents/${document.id}`),
      error: () => {
        this.message.set('File upload failed!');
        this.uploading.set(false);
      },
    });
  }
}
