import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { PLATFORM_ID, inject } from '@angular/core';
import { isPlatformServer } from '@angular/common';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import { UserService } from './user.service';

/** Adds the access token to calls to our API only (never to other origins) and surfaces permission refusals. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (isPlatformServer(inject(PLATFORM_ID)) || !req.url.startsWith(`${environment.apiBaseUrl}/`)) {
    return next(req);
  }

  const auth = inject(AuthService);
  const user = inject(UserService);

  return from(auth.getAccessToken()).pipe(
    switchMap((token) => next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }))),
    catchError((err: unknown) => {
      // A 403 from /me is handled as "no access" by UserService instead.
      if (err instanceof HttpErrorResponse && err.status === 403 && !req.url.endsWith('/me')) {
        user.forbidden.set(true);
      }
      return throwError(() => err);
    }),
  );
};
