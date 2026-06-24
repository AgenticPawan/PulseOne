import { Routes } from '@angular/router';
import { hostOperatorGuard } from './core/auth/host-operator.guard';

// Host admin routes (Phase 5): tenants, subscriptions/billing, audit, system health. Standalone,
// lazily-loaded components only (no NgModule).
//
// hostOperatorGuard is UI-only (CLAUDE.md security rule #4): it triggers sign-in and hides
// operator surfaces from non-operators. The authoritative gate is the server-side
// HostOperatorsOnly policy on the API. Route params/query are bound to component inputs via
// withComponentInputBinding() (see app.config.ts).
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'tenants' },

  {
    path: 'tenants',
    canActivate: [hostOperatorGuard],
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./features/tenants/tenant-list.component').then((m) => m.TenantListComponent),
      },
      {
        path: 'new',
        loadComponent: () =>
          import('./features/tenants/tenant-provision.component').then(
            (m) => m.TenantProvisionComponent,
          ),
      },
      {
        path: ':tenantId',
        loadComponent: () =>
          import('./features/tenants/tenant-detail.component').then(
            (m) => m.TenantDetailComponent,
          ),
      },
    ],
  },

  {
    path: 'subscriptions',
    canActivate: [hostOperatorGuard],
    loadComponent: () =>
      import('./features/subscriptions/subscription-dashboard.component').then(
        (m) => m.SubscriptionDashboardComponent,
      ),
  },

  {
    path: 'audit',
    canActivate: [hostOperatorGuard],
    loadComponent: () =>
      import('./features/audit/audit-browser.component').then((m) => m.AuditBrowserComponent),
  },

  {
    path: 'health',
    canActivate: [hostOperatorGuard],
    loadComponent: () =>
      import('./features/health/system-health.component').then((m) => m.SystemHealthComponent),
  },

  {
    path: 'forbidden',
    loadComponent: () =>
      import('./features/forbidden.component').then((m) => m.ForbiddenComponent),
  },

  { path: '**', redirectTo: 'tenants' },
];
