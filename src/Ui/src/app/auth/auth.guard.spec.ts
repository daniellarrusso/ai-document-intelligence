import { PLATFORM_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { vi } from 'vitest';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { UserService } from './user.service';

function setup({ authenticated = true, error = '', platformId = 'browser' } = {}) {
  const auth = { isAuthenticated: signal(authenticated), error: signal(error), login: vi.fn().mockResolvedValue(undefined) };
  const user = { ensureLoaded: vi.fn().mockResolvedValue(undefined) };
  TestBed.configureTestingModule({
    providers: [
      { provide: AuthService, useValue: auth },
      { provide: UserService, useValue: user },
      { provide: PLATFORM_ID, useValue: platformId },
    ],
  });
  const run = () =>
    TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url: '/claims/42' } as RouterStateSnapshot),
    );
  return { auth, user, run };
}

describe('authGuard', () => {
  it('lets a signed-in user through once their roles are loaded', async () => {
    const { user, run } = setup();

    expect(await run()).toBe(true);
    expect(user.ensureLoaded).toHaveBeenCalled();
  });

  it('sends a signed-out user to sign in, returning to the requested page', async () => {
    const { auth, user, run } = setup({ authenticated: false });

    expect(await run()).toBe(false);
    expect(auth.login).toHaveBeenCalledWith('/claims/42');
    expect(user.ensureLoaded).not.toHaveBeenCalled();
  });

  it('does not redirect again after a sign-in error, so a misconfiguration cannot loop', async () => {
    const { auth, run } = setup({ authenticated: false, error: 'Sign-in failed.' });

    expect(await run()).toBe(true);
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('lets the server prerender without a user', async () => {
    const { auth, user, run } = setup({ authenticated: false, platformId: 'server' });

    expect(await run()).toBe(true);
    expect(auth.login).not.toHaveBeenCalled();
    expect(user.ensureLoaded).not.toHaveBeenCalled();
  });
});
