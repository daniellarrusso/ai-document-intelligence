import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { ClaimDetails } from './claims/claim-details/claim-details';
import { Claims } from './claims/claims';
import { DocumentDetails } from './document-details/document-details';
import { Home } from './home/home';

export const routes: Routes = [
  {
    // Every page needs a signed-in user.
    path: '',
    canActivateChild: [authGuard],
    children: [
      { path: '', component: Home },
      { path: 'claims', component: Claims },
      { path: 'claims/:id', component: ClaimDetails },
      { path: 'documents/:id', component: DocumentDetails },
    ],
  },
  { path: '**', redirectTo: '' },
];
