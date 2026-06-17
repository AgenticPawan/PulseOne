import { Injectable, computed, inject, signal } from '@angular/core';
import { MsalService } from '@azure/msal-angular';
import {
  AccountInfo,
  AuthenticationResult,
  EventMessage,
  EventType,
  InteractionStatus,
  RedirectRequest,
} from '@azure/msal-browser';
import { getAuthConfig } from './auth.config';

/**
 * Tenant-portal auth facade. State is exposed as signals (CLAUDE.md: signals for state). MSAL owns
 * the token lifecycle (silent acquisition + refresh-token rotation handled by the IdP); this
 * service only surfaces account state and login/logout intents.
 *
 * SECURITY: any claim read here (tenant, roles) is for UI purposes ONLY. The authoritative checks
 * are server-side (TenantResolutionMiddleware + PBAC + HostOperatorsOnly).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly msal = inject(MsalService);

  private readonly _account = signal<AccountInfo | null>(null);

  readonly account = this._account.asReadonly();
  readonly isAuthenticated = computed(() => this._account() !== null);
  readonly tenantId = computed(
    () => (this._account()?.idTokenClaims as Record<string, unknown> | undefined)?.['extension_tenant_id'] as string | undefined,
  );

  /** Wire MSAL events to the account signal. Call once during app initialization. */
  initialize(): void {
    this.syncAccount();
    this.msal.instance.addEventCallback((message: EventMessage) => {
      if (
        message.eventType === EventType.LOGIN_SUCCESS ||
        message.eventType === EventType.ACQUIRE_TOKEN_SUCCESS ||
        message.eventType === EventType.SSO_SILENT_SUCCESS
      ) {
        const payload = message.payload as AuthenticationResult;
        this.msal.instance.setActiveAccount(payload.account);
      }
      if (message.eventType === EventType.LOGOUT_SUCCESS) {
        this.msal.instance.setActiveAccount(null);
      }
      this.syncAccount();
    });
  }

  login(): void {
    const cfg = getAuthConfig();
    const request: RedirectRequest = { scopes: [cfg.apiScope] };
    this.msal.loginRedirect(request);
  }

  logout(): void {
    this.msal.logoutRedirect();
  }

  private syncAccount(): void {
    const active = this.msal.instance.getActiveAccount() ?? this.msal.instance.getAllAccounts()[0] ?? null;
    if (active && !this.msal.instance.getActiveAccount()) {
      this.msal.instance.setActiveAccount(active);
    }
    this._account.set(active);
  }
}

export { InteractionStatus };
