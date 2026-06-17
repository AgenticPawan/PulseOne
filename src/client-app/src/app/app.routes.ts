import { Routes } from '@angular/router';

// Tenant portal routes (Phase 6). Standalone, lazy-loaded components only — no NgModules.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./app').then((m) => m.App),
  },
];
