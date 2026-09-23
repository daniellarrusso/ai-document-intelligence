import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { DocumentList } from './document-list/document-list';
import { UploadDocument } from './upload-document/upload-document';
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, UploadDocument, DocumentList],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly title = signal('Ui');
}
