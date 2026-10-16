import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { Claim } from '../claims/claim.model';
import { environment } from '../../environments/environment';
import { DocumentAnswer, DocumentQuery, DocumentSummary } from './document.model';

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

  /** Searches the documents the caller may see; `unmatched` restricts it to documents not linked to any claim. */
  searchDocuments(query: DocumentQuery): Observable<DocumentSummary[]> {
    let params = new HttpParams().set('pageNumber', query.pageNumber).set('pageSize', query.pageSize);
    if (query.search) {
      params = params.set('q', query.search);
    }
    if (query.unmatched) {
      params = params.set('unmatched', true);
    }

    return this.http.get<DocumentSummary[]>(this.baseUrl, { params });
  }

  /** The claims this document is linked to that the caller may see; empty means the document is unmatched. */
  getClaims(id: string): Observable<Claim[]> {
    return this.http.get<Claim[]>(`${this.baseUrl}/${id}/claims`);
  }

  getDocument(id: string): Observable<DocumentSummary> {
    return this.http.get<DocumentSummary>(`${this.baseUrl}/${id}`);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  ask(id: string, question: string): Observable<DocumentAnswer> {
    return this.http.post<DocumentAnswer>(`${this.baseUrl}/${id}/ask`, { question });
  }

  upload(file: File): Observable<DocumentSummary> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http
      .post<DocumentSummary>(this.baseUrl, formData)
      .pipe(tap(() => this.uploadCount.update((count) => count + 1)));
  }
}
