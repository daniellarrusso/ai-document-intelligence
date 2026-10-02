import { Routes } from '@angular/router';
import { Claims } from './claims/claims';
import { DocumentDetails } from './document-details/document-details';
import { Home } from './home/home';

export const routes: Routes = [
  { path: '', component: Home },
  { path: 'claims', component: Claims },
  { path: 'documents/:id', component: DocumentDetails },
  { path: '**', redirectTo: '' },
];
