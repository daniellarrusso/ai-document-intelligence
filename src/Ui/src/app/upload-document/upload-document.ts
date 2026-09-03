import { CommonModule } from '@angular/common';
import { Component, signal } from '@angular/core';

@Component({
  selector: 'app-upload-document',
  imports: [CommonModule],
  templateUrl: './upload-document.html',
  styleUrl: './upload-document.css',
})
export class UploadDocument {
  protected readonly title = 'Upload Document';

  selectedFile: File | null = null;
  uploading = signal(false);
  message = signal('');

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

    this.uploading.set(true);
    this.message.set('');

    // Connect to your backend service to upload the file here. 'http://localhost:5265/api/document'
    const formData = new FormData();
    formData.append('file', this.selectedFile);

    fetch('http://localhost:5265/api/document', {
      method: 'POST',
      body: formData,
    })
      .then((response) => {
        console.log('Response status:', response.status);
        if (!response.ok) {
          throw new Error('Network response was not ok');
        }
        return response.json();
      })
      .then((data) => {
        console.log('Upload response data:', data);
        this.message.set('File uploaded successfully!');
        this.selectedFile = null;
      })
      .catch((error) => {
        this.message.set('File upload failed!');
      })
      .finally(() => {
        this.uploading.set(false);
      });
  }
}
