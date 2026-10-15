import { isPlatformServer } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';
import { CanActivateChildFn } from '@angular/router';
import { AuthService } from './auth.service';
import { UserService } from './user.service';

/** Sends signed-out users to Entra and waits for their roles before any page renders. */
export const authGuard: CanActivateChildFn = async (_route, state) => {
  // Prerendering has no user; the pages load their data in the browser, where the API enforces access.
  if (isPlatformServer(inject(PLATFORM_ID))) {
    return true;
  }

  const auth = inject(AuthService);
  if (auth.error()) {
    // Let the shell show the error instead of redirecting again (a misconfiguration would loop).
    return true;
  }

  if (!auth.isAuthenticated()) {
    await auth.login(state.url);
    return false;
  }

  await inject(UserService).ensureLoaded();
  return true;
};
