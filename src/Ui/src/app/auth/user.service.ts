import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { WRITE_ROLES } from './roles';

export interface CurrentUser {
  name: string | null;
  roles: string[];
}

/** The signed-in user as the API sees them. Roles come from the API (validated server-side), not from decoding tokens. */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly http = inject(HttpClient);
  private readonly meUrl = `${environment.apiBaseUrl}/me`;
  private loading: Promise<void> | null = null;

  readonly user = signal<CurrentUser | null>(null);

  /** The account is signed in but has no role for this app, so the API refuses everything. */
  readonly noAccess = signal(false);

  /** An API call was refused for lack of permission; shown as a dismissible notice. */
  readonly forbidden = signal(false);

  readonly loadError = signal('');

  /** Whether the UI should offer actions that create or change data. The API enforces this independently. */
  readonly canWrite = computed(() => this.user()?.roles.some((role) => WRITE_ROLES.includes(role)) ?? false);

  /** Loads the user once; later calls reuse the result (a failed load can be retried). */
  ensureLoaded(): Promise<void> {
    this.loading ??= this.load();
    return this.loading;
  }

  reset(): void {
    this.loading = null;
    this.user.set(null);
    this.noAccess.set(false);
    this.forbidden.set(false);
    this.loadError.set('');
  }

  private async load(): Promise<void> {
    this.loadError.set('');
    try {
      this.user.set(await firstValueFrom(this.http.get<CurrentUser>(this.meUrl)));
    } catch (err) {
      this.loading = null;
      if (err instanceof HttpErrorResponse && err.status === 403) {
        this.noAccess.set(true);
      } else {
        this.loadError.set('Unable to load your account. Please try again.');
      }
    }
  }
}
