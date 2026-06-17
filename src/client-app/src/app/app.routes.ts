import { Routes } from '@angular/router';
import { MsalGuard } from '@azure/msal-angular';

// Tenant portal routes (Phase 6). Standalone, lazy-loaded components only — no NgModules.
//
// MsalGuard is a UI-only convenience: it triggers an interactive sign-in for unauthenticated
// users. It is NOT a security boundary — every tenant API call is independently authenticated
// and tenant-scoped server-side (TenantResolutionMiddleware + PBAC).
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    canActivate: [MsalGuard],
    loadComponent: () =>
      import('./app').then((m) => m.App),
  },
];
