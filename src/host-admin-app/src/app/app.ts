import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { EnvironmentService } from './core/services/environment.service';

interface NavItem {
  readonly label: string;
  readonly path: string;
  readonly icon: string;
}

/**
 * Host admin portal shell: persistent sidebar (Tenants, Subscriptions, Billing, Audit, System
 * Health) + top bar with operator identity and an environment badge. Layout is Tailwind utility
 * classes only (no component-scoped CSS), per the host-portal conventions.
 *
 * Navigation visibility here is UI convenience only; the real boundary is the server-side
 * `HostOperatorsOnly` policy (CLAUDE.md security rule #4).
 */
@Component({
  selector: 'po-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-screen bg-slate-50 text-slate-900">
      <!-- Sidebar -->
      <aside
        class="flex w-60 flex-col border-r border-slate-200 bg-white"
        aria-label="Primary navigation"
      >
        <div class="flex h-14 items-center gap-2 border-b border-slate-200 px-4">
          <span class="h-2.5 w-2.5 rounded-full bg-indigo-600" aria-hidden="true"></span>
          <span class="text-sm font-semibold tracking-tight">PulseOne Host</span>
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
          <h1 class="text-base font-semibold">Operator Console</h1>
          <div class="flex items-center gap-4">
            <span
              class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold uppercase ring-1 ring-inset"
              [class]="envBadgeClasses()"
              [attr.aria-label]="'Environment: ' + environment.current"
            >
              {{ environment.current }}
            </span>
            <div class="flex items-center gap-2">
              <span class="text-sm text-slate-600">{{ operatorName() }}</span>
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
          </div>
        </header>

        <main class="flex-1 overflow-auto p-6">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly environment = inject(EnvironmentService);

  protected readonly navItems: readonly NavItem[] = [
    { label: 'Tenants', path: '/tenants', icon: '🏢' },
    { label: 'Subscriptions', path: '/subscriptions', icon: '💳' },
    { label: 'Audit', path: '/audit', icon: '📋' },
    { label: 'System Health', path: '/health', icon: '🩺' },
  ];

  protected readonly operatorName = computed(
    () => this.auth.account()?.name ?? this.auth.account()?.username ?? 'Operator',
  );

  protected envBadgeClasses(): string {
    return this.environment.badgeClasses();
  }
}
