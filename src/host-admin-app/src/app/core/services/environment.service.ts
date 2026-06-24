import { Injectable } from '@angular/core';

export type DeployEnvironment = 'dev' | 'staging' | 'prod';

declare global {
  interface Window {
    __PULSEONE_HOST_ENV__?: DeployEnvironment;
  }
}

/**
 * Surfaces the deployment environment for the top-bar badge. The value is injected at deploy time
 * via `window.__PULSEONE_HOST_ENV__` (Key Vault-backed app settings, like the auth config). It is
 * cosmetic only — no authorization decision is ever made from it.
 */
@Injectable({ providedIn: 'root' })
export class EnvironmentService {
  readonly current: DeployEnvironment =
    (typeof window !== 'undefined' && window.__PULSEONE_HOST_ENV__) || 'dev';

  /** Tailwind colour classes for the environment badge. prod is intentionally loud. */
  badgeClasses(): string {
    switch (this.current) {
      case 'prod':
        return 'bg-red-100 text-red-800 ring-red-600/30';
      case 'staging':
        return 'bg-amber-100 text-amber-800 ring-amber-600/30';
      default:
        return 'bg-emerald-100 text-emerald-800 ring-emerald-600/30';
    }
  }
}
