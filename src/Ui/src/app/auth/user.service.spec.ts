import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { UserService } from './user.service';

const meUrl = `${environment.apiBaseUrl}/me`;

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { user: TestBed.inject(UserService), http: TestBed.inject(HttpTestingController) };
}

describe('UserService', () => {
  it('loads the user from the API once and shares the result', async () => {
    const { user, http } = setup();

    const first = user.ensureLoaded();
    const second = user.ensureLoaded();
    http.expectOne(meUrl).flush({ name: 'Jane Doe', roles: ['Handler'] });
    await Promise.all([first, second]);

    expect(user.user()).toEqual({ name: 'Jane Doe', roles: ['Handler'] });
    http.verify();
  });

  it.each([
    [['Reader'], false],
    [['Handler'], true],
    [['Admin'], true],
    [['Reader', 'Handler'], true],
    [[], false],
    [['Superuser'], false],
  ])('canWrite for roles %j is %s', async (roles, expected) => {
    const { user, http } = setup();

    const loaded = user.ensureLoaded();
    http.expectOne(meUrl).flush({ name: 'x', roles });
    await loaded;

    expect(user.canWrite()).toBe(expected);
  });

  it('cannot write before the user has loaded', () => {
    const { user } = setup();

    expect(user.canWrite()).toBe(false);
  });

  it('flags an account with no role when the API answers 403', async () => {
    const { user, http } = setup();

    const loaded = user.ensureLoaded();
    http.expectOne(meUrl).flush(null, { status: 403, statusText: 'Forbidden' });
    await loaded;

    expect(user.noAccess()).toBe(true);
    expect(user.loadError()).toBe('');
    expect(user.canWrite()).toBe(false);
  });

  it('reports other failures and allows a retry', async () => {
    const { user, http } = setup();

    const failed = user.ensureLoaded();
    http.expectOne(meUrl).flush(null, { status: 500, statusText: 'Server Error' });
    await failed;
    expect(user.loadError()).toContain('Unable to load your account');
    expect(user.noAccess()).toBe(false);

    const retried = user.ensureLoaded();
    http.expectOne(meUrl).flush({ name: 'Jane', roles: ['Reader'] });
    await retried;

    expect(user.loadError()).toBe('');
    expect(user.user()?.name).toBe('Jane');
  });

  it('forgets everything on reset', async () => {
    const { user, http } = setup();
    const loaded = user.ensureLoaded();
    http.expectOne(meUrl).flush({ name: 'Jane', roles: ['Admin'] });
    await loaded;
    user.forbidden.set(true);

    user.reset();

    expect(user.user()).toBeNull();
    expect(user.forbidden()).toBe(false);
    expect(user.canWrite()).toBe(false);
  });
});
