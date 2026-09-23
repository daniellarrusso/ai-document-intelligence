import { Component, inject, signal } from '@angular/core';
import { DocumentService } from '../document-list/document.service';

@Component({
  selector: 'app-upload-document',
  templateUrl: './upload-document.html',
  styleUrl: './upload-document.css',
})
export class UploadDocument {
  private readonly documentService = inject(DocumentService);

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

    this.documentService.upload(file).subscribe({
      next: () => {
        this.message.set('File uploaded successfully!');
        this.selectedFile.set(null);
        this.uploading.set(false);
      },
      error: () => {
        this.message.set('File upload failed!');
        this.uploading.set(false);
      },
    });
  }
}
