import { Component, afterNextRender, inject, signal } from '@angular/core';
import { UserService } from '../auth/user.service';
import { ClaimList } from './claim-list/claim-list';
import { CreateClaim } from './create-claim/create-claim';

@Component({
  selector: 'app-claims',
  imports: [CreateClaim, ClaimList],
  templateUrl: './claims.html',
})
export class Claims {
  protected readonly user = inject(UserService);

  // Roles load before the first browser render but not during prerender; wait so the first client render matches the server HTML.
  protected readonly browserReady = signal(false);

  constructor() {
    afterNextRender(() => this.browserReady.set(true));
  }
}
