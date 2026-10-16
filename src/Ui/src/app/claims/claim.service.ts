import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { Claim, ClaimDetail, ClaimDocument, ClaimStatus, CreateClaimRequest, Handler } from './claim.model';

export interface ClaimQuery {
  search?: string;
  status?: ClaimStatus | null;
  pageNumber: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class ClaimService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/claims`;
  private readonly createdCount = signal(0);

  /** Increments after every successful create so listeners can refresh. */
  readonly created = this.createdCount.asReadonly();

  getClaims(query: ClaimQuery): Observable<Claim[]> {
    let params = new HttpParams().set('pageNumber', query.pageNumber).set('pageSize', query.pageSize);
    if (query.search) {
      params = params.set('q', query.search);
    }
    if (query.status !== undefined && query.status !== null) {
      params = params.set('status', query.status);
    }

    return this.http.get<Claim[]>(this.baseUrl, { params });
  }

  getClaim(id: string): Observable<ClaimDetail> {
    return this.http.get<ClaimDetail>(`${this.baseUrl}/${id}`);
  }

  /** Uploads a file as a new document of the claim; it is then processed like any other document. */
  uploadDocument(claimId: string, file: File): Observable<ClaimDocument> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<ClaimDocument>(`${this.baseUrl}/${claimId}/documents`, formData);
  }

  /** Links an existing document to the claim. The document can stay linked to other claims as well. */
  linkDocument(claimId: string, documentId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${claimId}/documents/${documentId}`, null);
  }

  /** Removes the link only; the document itself is kept (and becomes unmatched if this was its last claim). */
  unlinkDocument(claimId: string, documentId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${claimId}/documents/${documentId}`);
  }

  /** The people a claim can be assigned to (Handlers and Admins only; the API refuses Readers). */
  getHandlers(): Observable<Handler[]> {
    return this.http.get<Handler[]>(`${environment.apiBaseUrl}/users/handlers`);
  }

  /** Hands the claim to another handler. The previous handler can no longer see it afterwards. */
  assignClaim(claimId: string, userId: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${claimId}/assignee`, { userId });
  }

  setPrivacy(claimId: string, isPrivate: boolean): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${claimId}/privacy`, { isPrivate });
  }

  createClaim(request: CreateClaimRequest): Observable<Claim> {
    return this.http
      .post<Claim>(this.baseUrl, request)
      .pipe(tap(() => this.createdCount.update((count) => count + 1)));
  }
}
