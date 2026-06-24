import { Injectable, signal } from '@angular/core';

/**
 * Per-subdomain tenant theme (prompt 01: "read `--tenant-primary` and `--tenant-accent` CSS
 * variables injected by the server").
 *
 * The host page may inject a non-secret `window.__PULSEONE_TENANT_THEME__` blob at deploy time
 * (per-subdomain config, like the auth config). When present we set the corresponding CSS custom
 * properties on :root so Tailwind arbitrary-value utilities (e.g. `bg-[var(--tenant-primary)]`) and
 * Razorpay Checkout's theme colour pick them up. When absent we keep the neutral brand defaults
 * already declared in styles.scss — we NEVER bake a per-tenant value into the bundle.
 */
export interface TenantThemeConfig {
  readonly tenantName?: string;
  readonly primary?: string;
  readonly accent?: string;
  readonly logoUrl?: string;
}

declare global {
  interface Window {
    __PULSEONE_TENANT_THEME__?: TenantThemeConfig;
  }
}

@Injectable({ providedIn: 'root' })
export class TenantThemeService {
  private readonly _tenantName = signal<string | null>(null);
  private readonly _logoUrl = signal<string | null>(null);

  readonly tenantName = this._tenantName.asReadonly();
  readonly logoUrl = this._logoUrl.asReadonly();

  /**
   * Applies the server-injected theme to the document root. Called once during app initialization.
   * Values are validated to a conservative CSS-colour shape before being written, so a tampered
   * blob can never inject arbitrary CSS.
   */
  apply(): void {
    const cfg = typeof window !== 'undefined' ? window.__PULSEONE_TENANT_THEME__ : undefined;
    if (!cfg) {
      return;
    }

    const root = document.documentElement;
    if (this.isSafeColor(cfg.primary)) {
      root.style.setProperty('--tenant-primary', cfg.primary!);
    }
    if (this.isSafeColor(cfg.accent)) {
      root.style.setProperty('--tenant-accent', cfg.accent!);
    }
    if (cfg.tenantName) {
      this._tenantName.set(cfg.tenantName);
    }
    if (cfg.logoUrl) {
      this._logoUrl.set(cfg.logoUrl);
    }
  }

  /** Accepts only `#rgb` / `#rrggbb` hex colours — rejects anything that could smuggle CSS. */
  private isSafeColor(value: string | undefined): boolean {
    return !!value && /^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(value);
  }
}
