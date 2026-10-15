import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { UserService } from '../auth/user.service';
import { ClaimService } from './claim.service';
import { Claims } from './claims';

async function render(canWrite: boolean) {
  await TestBed.configureTestingModule({
    imports: [Claims],
    providers: [
      provideRouter([]),
      { provide: ClaimService, useValue: { getClaims: () => of([]), createClaim: () => of({}), created: signal(0) } },
      { provide: UserService, useValue: { canWrite: signal(canWrite) } },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(Claims);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('Claims', () => {
  it('offers the create form and the list to someone who can write', async () => {
    const el = await render(true);

    expect(el.querySelector('app-create-claim')).not.toBeNull();
    expect(el.querySelector('app-claim-list')).not.toBeNull();
  });

  it('hides the create form from a read-only user but still lists claims', async () => {
    const el = await render(false);

    expect(el.querySelector('app-create-claim')).toBeNull();
    expect(el.querySelector('app-claim-list')).not.toBeNull();
  });
});
