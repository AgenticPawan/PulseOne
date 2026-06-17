import { Routes } from '@angular/router';

// Host admin routes (Phase 5): billing, tenants, pricing. Standalone components only.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  {
    path: 'overview',
    loadComponent: () =>
      import('./app').then((m) => m.App),
  },
];
