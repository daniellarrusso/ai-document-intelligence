import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AccountInfo } from '@azure/msal-browser';
import { vi } from 'vitest';
import { environment } from '../../environments/environment';
import { MSAL_FACTORY } from './auth.config';
import { AuthService } from './auth.service';

const account = { homeAccountId: 'h', environment: 'e', tenantId: 't', username: 'jane@contoso.com', localAccountId: 'l' } as AccountInfo;
const scopes = [environment.auth.apiScope];

function fakeMsal(overrides: Record<string, unknown> = {}) {
  return {
    handleRedirectPromise: vi.fn().mockResolvedValue(null),
    getActiveAccount: vi.fn().mockReturnValue(null),
    getAllAccounts: vi.fn().mockReturnValue([]),
    setActiveAccount: vi.fn(),
    loginRedirect: vi.fn().mockResolvedValue(undefined),
    logoutRedirect: vi.fn().mockResolvedValue(undefined),
    acquireTokenSilent: vi.fn().mockResolvedValue({ accessToken: 'silent-token' }),
    acquireTokenRedirect: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

function setup(msal: ReturnType<typeof fakeMsal>, platformId = 'browser') {
  const factory = vi.fn().mockResolvedValue(msal);
  TestBed.configureTestingModule({
    providers: [
      { provide: MSAL_FACTORY, useValue: factory },
      { provide: PLATFORM_ID, useValue: platformId },
    ],
  });
  return { auth: TestBed.inject(AuthService), factory };
}

describe('AuthService', () => {
  describe('initialize', () => {
    it('uses the account from a completed sign-in redirect and makes it active', async () => {
      const msal = fakeMsal({ handleRedirectPromise: vi.fn().mockResolvedValue({ account }) });
      const { auth } = setup(msal);

      await auth.initialize();

      expect(auth.account()).toBe(account);
      expect(auth.isAuthenticated()).toBe(true);
      expect(msal.setActiveAccount).toHaveBeenCalledWith(account);
    });

    it('falls back to an account cached from an earlier visit', async () => {
      const msal = fakeMsal({ getAllAccounts: vi.fn().mockReturnValue([account]) });
      const { auth } = setup(msal);

      await auth.initialize();

      expect(auth.account()).toBe(account);
    });

    it('stays signed out when there is no account', async () => {
      const { auth } = setup(fakeMsal());

      await auth.initialize();

      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.error()).toBe('');
    });

    it('reports an error instead of retrying when Entra returns one to the redirect', async () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const msal = fakeMsal({ handleRedirectPromise: vi.fn().mockRejectedValue(new Error('AADSTS50011')) });
      const { auth } = setup(msal);

      await auth.initialize();

      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.error()).toContain('Sign-in failed');
    });

    it('does nothing on the server', async () => {
      const { auth, factory } = setup(fakeMsal(), 'server');

      await auth.initialize();

      expect(factory).not.toHaveBeenCalled();
      expect(auth.isAuthenticated()).toBe(false);
    });
  });

  describe('login and logout', () => {
    it('redirects to sign in requesting the API scope and returning to the given page', async () => {
      const msal = fakeMsal();
      const { auth } = setup(msal);
      await auth.initialize();

      await auth.login('/claims/42');

      expect(msal.loginRedirect).toHaveBeenCalledWith({
        scopes,
        redirectStartPage: `${window.location.origin}/claims/42`,
      });
    });

    it('clears an earlier error when signing in again', async () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const msal = fakeMsal({ handleRedirectPromise: vi.fn().mockRejectedValue(new Error('x')) });
      const { auth } = setup(msal);
      await auth.initialize();
      expect(auth.error()).not.toBe('');

      await auth.login();

      expect(auth.error()).toBe('');
    });

    it('signs out the current account and returns to the app', async () => {
      const msal = fakeMsal({ handleRedirectPromise: vi.fn().mockResolvedValue({ account }) });
      const { auth } = setup(msal);
      await auth.initialize();

      await auth.logout();

      expect(msal.logoutRedirect).toHaveBeenCalledWith({ account, postLogoutRedirectUri: window.location.origin });
    });
  });

  describe('getAccessToken', () => {
    async function signedIn(overrides: Record<string, unknown> = {}) {
      const msal = fakeMsal({ handleRedirectPromise: vi.fn().mockResolvedValue({ account }), ...overrides });
      const { auth } = setup(msal);
      await auth.initialize();
      return { auth, msal };
    }

    it('returns the silently acquired token for the API scope', async () => {
      const { auth, msal } = await signedIn();

      expect(await auth.getAccessToken()).toBe('silent-token');
      expect(msal.acquireTokenSilent).toHaveBeenCalledWith({ scopes, account });
    });

    it('falls back to interactive sign-in when silent renewal fails, and abandons the request', async () => {
      const failure = new Error('interaction_required');
      const { auth, msal } = await signedIn({ acquireTokenSilent: vi.fn().mockRejectedValue(failure) });

      await expect(auth.getAccessToken()).rejects.toBe(failure);

      expect(msal.acquireTokenRedirect).toHaveBeenCalledWith(expect.objectContaining({ scopes, account }));
    });

    it('rejects when nobody is signed in', async () => {
      const { auth } = setup(fakeMsal());
      await auth.initialize();

      await expect(auth.getAccessToken()).rejects.toThrow('Not signed in');
    });
  });
});
