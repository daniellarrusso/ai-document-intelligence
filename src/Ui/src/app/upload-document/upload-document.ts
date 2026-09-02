import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

@Component({
  selector: 'app-upload-document',
  imports: [CommonModule],
  templateUrl: './upload-document.html',
  styleUrl: './upload-document.css',
})
export class UploadDocument {
  protected readonly title = 'Upload Document';

  selectedFile: File | null = null;
  uploading: boolean = false;
  message: string | null = null;

  constructor() {}

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
    }
  }

  upload(): void {
    if (!this.selectedFile) {
      return;
    }

    this.uploading = true;
    this.message = null;

    // Connect to your backend service to upload the file here.
    setTimeout(() => {
      this.uploading = false;
      this.message = 'File uploaded successfully!';
      this.selectedFile = null;
    }, 2000);
  }
}
