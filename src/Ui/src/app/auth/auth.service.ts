import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { AccountInfo, IPublicClientApplication, InteractionRequiredAuthError } from '@azure/msal-browser';
import { API_SCOPES, MSAL_FACTORY, buildMsalConfig } from './auth.config';

/**
 * Entra ID sign-in via MSAL (authorization code + PKCE, redirect flow). Browser only: on the server
 * (SSR/prerender) every method is a no-op, because no one is signed in there.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly createMsal = inject(MSAL_FACTORY);
  private msal: IPublicClientApplication | null = null;

  private readonly accountSignal = signal<AccountInfo | null>(null);
  private readonly errorSignal = signal('');

  readonly account = this.accountSignal.asReadonly();
  readonly isAuthenticated = computed(() => this.accountSignal() !== null);

  /** Set when Entra returned an error to the redirect (e.g. a misconfigured app). Stops automatic sign-in retries. */
  readonly error = this.errorSignal.asReadonly();

  /** Runs once at startup: completes a sign-in redirect if one is in progress and restores any cached account. */
  async initialize(): Promise<void> {
    if (!this.isBrowser) {
      return;
    }

    try {
      this.msal = await this.createMsal(buildMsalConfig(window.location.origin));
      const result = await this.msal.handleRedirectPromise();
      const account = result?.account ?? this.msal.getActiveAccount() ?? this.msal.getAllAccounts()[0] ?? null;
      if (account) {
        this.msal.setActiveAccount(account);
      }
      this.accountSignal.set(account);
    } catch (err) {
      // Don't retry sign-in automatically: a persistent error (such as a redirect URI mismatch) would loop forever.
      console.error('Sign-in failed', err);
      this.errorSignal.set('Sign-in failed. Check the console for details, then try again.');
    }
  }

  /** Redirects to Entra to sign in, then returns to `returnUrl`. */
  async login(returnUrl = '/'): Promise<void> {
    this.errorSignal.set('');
    await this.msal?.loginRedirect({
      scopes: API_SCOPES,
      redirectStartPage: window.location.origin + returnUrl,
    });
  }

  async logout(): Promise<void> {
    await this.msal?.logoutRedirect({
      account: this.accountSignal(),
      postLogoutRedirectUri: window.location.origin,
    });
  }

  /** A valid access token for the API, renewed silently when possible. */
  async getAccessToken(): Promise<string> {
    const account = this.accountSignal();
    if (!this.msal || !account) {
      throw new Error('Not signed in.');
    }

    try {
      const result = await this.msal.acquireTokenSilent({ scopes: API_SCOPES, account });
      return result.accessToken;
    } catch (err) {
      // Only an interaction-required failure (expired session, consent needed) is fixed by signing in again.
      // Anything else (network, configuration) would just redirect repeatedly, so let it propagate.
      if (!(err instanceof InteractionRequiredAuthError)) {
        throw err;
      }

      // The page navigates away, so the request is abandoned.
      await this.msal.acquireTokenRedirect({
        scopes: API_SCOPES,
        account,
        redirectStartPage: window.location.href,
      });
      throw err;
    }
  }
}
