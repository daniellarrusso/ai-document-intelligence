import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { UploadDocument } from './upload-document/upload-document';
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, UploadDocument],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly title = signal('Ui');
}
