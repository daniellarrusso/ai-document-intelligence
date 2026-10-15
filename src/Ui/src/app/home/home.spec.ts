import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { UserService } from '../auth/user.service';
import { Home } from './home';

async function render(canWrite: boolean) {
  await TestBed.configureTestingModule({
    imports: [Home],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: UserService, useValue: { canWrite: signal(canWrite) } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(Home);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('Home', () => {
  it('renders the upload form and document list for someone who can write', async () => {
    const el = await render(true);

    expect(el.querySelector('app-upload-document')).not.toBeNull();
    expect(el.querySelector('app-document-list')).not.toBeNull();
  });

  it('hides the upload form from a read-only user but still lists documents', async () => {
    const el = await render(false);

    expect(el.querySelector('app-upload-document')).toBeNull();
    expect(el.querySelector('app-document-list')).not.toBeNull();
  });
});
