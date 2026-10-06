import { InjectionToken } from '@angular/core';
import {
  Configuration,
  IPublicClientApplication,
  createStandardPublicClientApplication,
} from '@azure/msal-browser';
import { environment } from '../../environments/environment';

/** Delegated scope of the API; requested at sign-in and for every API call. */
export const API_SCOPES = [environment.auth.apiScope];

export function buildMsalConfig(origin: string): Configuration {
  return {
    auth: {
      clientId: environment.auth.clientId,
      authority: environment.auth.authority,
      redirectUri: origin,
      postLogoutRedirectUri: origin,
    },
    // Cleared when the tab closes, which limits how long a stolen-from-storage token would be useful.
    cache: { cacheLocation: 'sessionStorage' },
  };
}

/** Creates the MSAL client. Overridable so tests don't need a real browser auth stack. */
export const MSAL_FACTORY = new InjectionToken<(config: Configuration) => Promise<IPublicClientApplication>>(
  'MSAL_FACTORY',
  { providedIn: 'root', factory: () => createStandardPublicClientApplication },
);
