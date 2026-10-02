import { Component } from '@angular/core';
import { ClaimList } from './claim-list/claim-list';
import { CreateClaim } from './create-claim/create-claim';

@Component({
  selector: 'app-claims',
  imports: [CreateClaim, ClaimList],
  templateUrl: './claims.html',
})
export class Claims {}
