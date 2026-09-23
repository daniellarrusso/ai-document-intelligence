import { Routes } from '@angular/router';
import { DocumentDetails } from './document-details/document-details';
import { Home } from './home/home';

export const routes: Routes = [
  { path: '', component: Home },
  { path: 'documents/:id', component: DocumentDetails },
  { path: '**', redirectTo: '' },
];
