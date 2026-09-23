import { Component } from '@angular/core';
import { DocumentList } from '../document-list/document-list';
import { UploadDocument } from '../upload-document/upload-document';

@Component({
  selector: 'app-home',
  imports: [UploadDocument, DocumentList],
  templateUrl: './home.html',
})
export class Home {}
