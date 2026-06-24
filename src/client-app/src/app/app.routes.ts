import { Routes } from '@angular/router';
import { MsalGuard } from '@azure/msal-angular';

// Tenant portal routes (Phase 6). Standalone, lazy-loaded components only — no NgModules.
//
// MsalGuard is a UI-only convenience: it triggers an interactive sign-in for unauthenticated
// users. It is NOT a security boundary — every tenant API call is independently authenticated
// and tenant-scoped server-side (TenantResolutionMiddleware + named EF Core query filters + PBAC).
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    canActivate: [MsalGuard],
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
  },
  {
    path: 'reports',
    canActivate: [MsalGuard],
    loadComponent: () =>
      import('./features/reports/report-grid.component').then((m) => m.ReportGridComponent),
  },
  {
    path: 'billing',
    canActivate: [MsalGuard],
    loadComponent: () =>
      import('./features/billing/billing-page.component').then((m) => m.BillingPageComponent),
  },
  {
    path: 'team',
    canActivate: [MsalGuard],
    loadComponent: () => import('./features/team/team.component').then((m) => m.TeamComponent),
  },
  {
    path: 'settings',
    canActivate: [MsalGuard],
    loadComponent: () =>
      import('./features/settings/settings.component').then((m) => m.SettingsComponent),
  },
  { path: '**', redirectTo: 'dashboard' },
];
