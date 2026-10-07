import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { environment } from '../../environments/environment';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { UserService } from './user.service';

const apiUrl = `${environment.apiBaseUrl}/claims`;

function setup(getAccessToken = vi.fn().mockResolvedValue('tok'), platformId = 'browser') {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: { getAccessToken } },
      { provide: PLATFORM_ID, useValue: platformId },
    ],
  });
  return {
    http: TestBed.inject(HttpClient),
    controller: TestBed.inject(HttpTestingController),
    user: TestBed.inject(UserService),
    getAccessToken,
  };
}

// The interceptor awaits an async token, so the request reaches the backend a microtask later.
const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve));

describe('authInterceptor', () => {
  it('sends the access token to the API', async () => {
    const { http, controller } = setup();

    http.get(apiUrl).subscribe();
    await flushMicrotasks();

    expect(controller.expectOne(apiUrl).request.headers.get('Authorization')).toBe('Bearer tok');
  });

  it('never sends the token to other origins', async () => {
    const { http, controller, getAccessToken } = setup();

    http.get('https://example.com/data').subscribe();
    await flushMicrotasks();

    expect(controller.expectOne('https://example.com/data').request.headers.has('Authorization')).toBe(false);
    expect(getAccessToken).not.toHaveBeenCalled();
  });

  it('does not treat a lookalike path as the API', async () => {
    const { http, controller, getAccessToken } = setup();

    http.get(`${environment.apiBaseUrl}x/claims`).subscribe();
    await flushMicrotasks();

    controller.expectOne(`${environment.apiBaseUrl}x/claims`);
    expect(getAccessToken).not.toHaveBeenCalled();
  });

  it('adds nothing on the server', async () => {
    const { http, controller, getAccessToken } = setup(undefined, 'server');

    http.get(apiUrl).subscribe();
    await flushMicrotasks();

    expect(controller.expectOne(apiUrl).request.headers.has('Authorization')).toBe(false);
    expect(getAccessToken).not.toHaveBeenCalled();
  });

  it('does not send the request when no token can be obtained', async () => {
    const { http, controller } = setup(vi.fn().mockRejectedValue(new Error('Not signed in.')));
    const error = vi.fn();

    http.get(apiUrl).subscribe({ error });
    await flushMicrotasks();

    controller.expectNone(apiUrl);
    expect(error).toHaveBeenCalled();
  });

  it('flags a permission refusal and still passes the error on', async () => {
    const { http, controller, user } = setup();
    const error = vi.fn();

    http.post(apiUrl, {}).subscribe({ error });
    await flushMicrotasks();
    controller.expectOne(apiUrl).flush(null, { status: 403, statusText: 'Forbidden' });

    expect(user.forbidden()).toBe(true);
    expect(error).toHaveBeenCalledWith(expect.objectContaining({ status: 403 }));
  });

  it('does not flag a 403 from /me, which means the account has no role', async () => {
    const { http, controller, user } = setup();

    http.get(`${environment.apiBaseUrl}/me`).subscribe({ error: () => undefined });
    await flushMicrotasks();
    controller.expectOne(`${environment.apiBaseUrl}/me`).flush(null, { status: 403, statusText: 'Forbidden' });

    expect(user.forbidden()).toBe(false);
  });

  it('does not flag other failures', async () => {
    const { http, controller, user } = setup();

    http.get(apiUrl).subscribe({ error: () => undefined });
    await flushMicrotasks();
    controller.expectOne(apiUrl).flush(null, { status: 500, statusText: 'Server Error' });

    expect(user.forbidden()).toBe(false);
  });
});
