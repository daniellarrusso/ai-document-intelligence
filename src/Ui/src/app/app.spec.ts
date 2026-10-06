import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { App } from './app';
import { AuthService } from './auth/auth.service';
import { CurrentUser, UserService } from './auth/user.service';

interface Setup {
  account?: { name?: string; username: string } | null;
  error?: string;
  user?: CurrentUser | null;
  noAccess?: boolean;
  forbidden?: boolean;
  loadError?: string;
}

async function render({ account = null, error = '', user = null, noAccess = false, forbidden = false, loadError = '' }: Setup = {}) {
  const auth = {
    account: signal(account),
    error: signal(error),
    isAuthenticated: signal(account !== null),
    login: vi.fn().mockResolvedValue(undefined),
    logout: vi.fn().mockResolvedValue(undefined),
  };
  const userService = {
    user: signal(user),
    noAccess: signal(noAccess),
    forbidden: signal(forbidden),
    loadError: signal(loadError),
    reset: vi.fn(),
  };

  await TestBed.configureTestingModule({
    imports: [App],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: AuthService, useValue: auth },
      { provide: UserService, useValue: userService },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(App);
  await fixture.whenStable();
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const button = (label: string) =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);

  return { fixture, el, auth, userService, button };
}

describe('App', () => {
  it('should create the app', async () => {
    const { fixture } = await render();

    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the header and a router outlet', async () => {
    const { el } = await render();

    expect(el.querySelector('h1')?.textContent).toContain('AI Document Intelligence');
    expect(el.querySelector('router-outlet')).not.toBeNull();
  });

  it('shows no user menu while signed out', async () => {
    const { el } = await render();

    expect(el.querySelector('header button')).toBeNull();
  });

  it('shows the signed-in user with their roles and a sign-out button', async () => {
    const { el } = await render({
      account: { username: 'jane@contoso.com' },
      user: { name: 'Jane Doe', roles: ['Handler', 'Reader'] },
    });

    const header = el.querySelector('header')!.textContent;
    expect(header).toContain('Jane Doe');
    expect(header).toContain('Handler');
    expect(header).toContain('Reader');
    expect(header).toContain('Sign out');
  });

  it('falls back to the account name before the API user has loaded', async () => {
    const { el } = await render({ account: { name: 'Jane From Token', username: 'jane@contoso.com' } });

    expect(el.querySelector('header')!.textContent).toContain('Jane From Token');
  });

  it('forgets the user and signs out when Sign out is clicked', async () => {
    const { auth, userService, button } = await render({
      account: { username: 'jane@contoso.com' },
      user: { name: 'Jane', roles: ['Reader'] },
    });

    button('Sign out')!.click();

    expect(userService.reset).toHaveBeenCalled();
    expect(auth.logout).toHaveBeenCalled();
  });

  it('shows a sign-in error instead of the page, with a way to try again', async () => {
    const { el, auth, button } = await render({ error: 'Sign-in failed. Check the console.' });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Sign-in failed');
    expect(el.querySelector('router-outlet')).toBeNull();

    button('Try again')!.click();
    expect(auth.login).toHaveBeenCalled();
  });

  it('explains when the account has no role, instead of showing the page', async () => {
    const { el } = await render({ account: { username: 'x@contoso.com' }, noAccess: true });

    expect(el.textContent).toContain('No access');
    expect(el.querySelector('router-outlet')).toBeNull();
  });

  it('shows a failure to load the account instead of the page', async () => {
    const { el } = await render({ account: { username: 'x@contoso.com' }, loadError: 'Unable to load your account. Please try again.' });

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Unable to load your account');
    expect(el.querySelector('router-outlet')).toBeNull();
  });

  it('shows a dismissible permission notice above the page', async () => {
    const { fixture, el, userService, button } = await render({
      account: { username: 'x@contoso.com' },
      user: { name: 'Rita', roles: ['Reader'] },
      forbidden: true,
    });
    expect(el.querySelector('[role="alert"]')?.textContent).toContain("You don't have permission");
    expect(el.querySelector('router-outlet')).not.toBeNull();

    button('Dismiss')!.click();
    fixture.detectChanges();

    expect(userService.forbidden()).toBe(false);
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });
});
