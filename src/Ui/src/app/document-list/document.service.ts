import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { DocumentSummary } from './document.model';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/documents`;
  private readonly uploadCount = signal(0);

  /** Increments after every successful upload so listeners can refresh. */
  readonly uploads = this.uploadCount.asReadonly();

  getDocuments(): Observable<DocumentSummary[]> {
    return this.http.get<DocumentSummary[]>(this.baseUrl);
  }

  upload(file: File): Observable<unknown> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http
      .post(this.baseUrl, formData)
      .pipe(tap(() => this.uploadCount.update((count) => count + 1)));
  }
}
