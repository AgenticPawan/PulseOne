import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { TenantThemeService } from './core/services/tenant-theme.service';

interface NavItem {
  readonly label: string;
  readonly path: string;
  readonly icon: string;
}

/**
 * Tenant portal shell (prompt 01): a persistent sidebar (Dashboard, Reports, Billing, Team,
 * Settings) plus a top bar with the signed-in user and a sign-out action. Includes an accessibility
 * skip link to the main content region.
 *
 * Theming uses the `--tenant-primary` / `--tenant-accent` CSS custom properties applied by
 * TenantThemeService from a server-injected per-subdomain config — referenced here via Tailwind
 * arbitrary-value utilities, never hardcoded per tenant.
 *
 * Navigation visibility is UI convenience only; the authoritative isolation boundary is server-side
 * (TenantResolutionMiddleware + PBAC). Styling is Tailwind utility classes only.
 */
@Component({
  selector: 'po-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      href="#main-content"
      class="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-50 focus:rounded-md focus:bg-white focus:px-3 focus:py-2 focus:text-sm focus:shadow"
    >
      Skip to main content
    </a>

    <div class="flex min-h-screen bg-slate-50 text-slate-900">
      <!-- Sidebar -->
      <aside
        class="flex w-60 flex-col border-r border-slate-200 bg-white"
        aria-label="Primary navigation"
      >
        <div class="flex h-14 items-center gap-2 border-b border-slate-200 px-4">
          @if (logoUrl()) {
            <img [src]="logoUrl()" alt="" class="h-6 w-6 rounded" />
          } @else {
            <span
              class="h-2.5 w-2.5 rounded-full bg-[var(--tenant-primary,#2563eb)]"
              aria-hidden="true"
            ></span>
          }
          <span class="truncate text-sm font-semibold tracking-tight">{{ portalName() }}</span>
        </div>
        <nav class="flex-1 space-y-1 p-3" aria-label="Sections">
          @for (item of navItems; track item.path) {
            <a
              [routerLink]="item.path"
              routerLinkActive="bg-indigo-50 text-indigo-700"
              [routerLinkActiveOptions]="{ exact: false }"
              class="flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
              [attr.aria-label]="item.label"
            >
              <span class="text-base" aria-hidden="true">{{ item.icon }}</span>
              {{ item.label }}
            </a>
          }
        </nav>
      </aside>

      <!-- Main column -->
      <div class="flex min-w-0 flex-1 flex-col">
        <header
          class="flex h-14 items-center justify-between border-b border-slate-200 bg-white px-6"
        >
          <h1 class="text-base font-semibold">{{ tenantName() ?? 'PulseOne' }}</h1>
          <div class="flex items-center gap-3">
            <span class="text-sm text-slate-600">{{ userName() }}</span>
            @if (auth.isAuthenticated()) {
              <button
                type="button"
                (click)="auth.logout()"
                class="rounded-md border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-700 hover:bg-slate-100"
                aria-label="Sign out"
              >
                Sign out
              </button>
            }
          </div>
        </header>

        <main id="main-content" class="flex-1 overflow-auto p-6" tabindex="-1">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly theme = inject(TenantThemeService);

  protected readonly navItems: readonly NavItem[] = [
    { label: 'Dashboard', path: '/dashboard', icon: '📊' },
    { label: 'Reports', path: '/reports', icon: '📄' },
    { label: 'Billing', path: '/billing', icon: '💳' },
    { label: 'Team', path: '/team', icon: '👥' },
    { label: 'Settings', path: '/settings', icon: '⚙️' },
  ];

  protected readonly tenantName = this.theme.tenantName;
  protected readonly logoUrl = this.theme.logoUrl;
  protected readonly portalName = computed(() => this.theme.tenantName() ?? 'PulseOne');

  protected readonly userName = computed(
    () => this.auth.account()?.name ?? this.auth.account()?.username ?? 'Account',
  );
}
