import { Component, afterNextRender, inject, signal } from '@angular/core';
import { UserService } from '../auth/user.service';
import { DocumentList } from '../document-list/document-list';
import { UploadDocument } from '../upload-document/upload-document';

@Component({
  selector: 'app-home',
  imports: [UploadDocument, DocumentList],
  templateUrl: './home.html',
})
export class Home {
  protected readonly user = inject(UserService);

  // Roles load before the first browser render but not during prerender; wait so the first client render matches the server HTML.
  protected readonly browserReady = signal(false);

  constructor() {
    afterNextRender(() => this.browserReady.set(true));
  }
}
