import { Routes } from '@angular/router';
import { hostOperatorGuard } from './core/auth/host-operator.guard';

// Host admin routes (Phase 5): billing, tenants, pricing. Standalone components only.
//
// hostOperatorGuard is UI-only (CLAUDE.md security rule #4): it triggers sign-in and hides
// operator surfaces from non-operators. The authoritative gate is the server-side
// HostOperatorsOnly policy on the API.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  {
    path: 'overview',
    canActivate: [hostOperatorGuard],
    loadComponent: () =>
      import('./app').then((m) => m.App),
  },
  {
    path: 'forbidden',
    loadComponent: () =>
      import('./app').then((m) => m.App),
  },
];
