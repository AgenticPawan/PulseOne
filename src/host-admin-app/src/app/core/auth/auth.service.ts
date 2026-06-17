import { Injectable, computed, inject, signal } from '@angular/core';
import { MsalService } from '@azure/msal-angular';
import {
  AccountInfo,
  AuthenticationResult,
  EventMessage,
  EventType,
  RedirectRequest,
} from '@azure/msal-browser';
import { getHostAuthConfig } from './auth.config';

/** Canonical claim values mirrored from the backend (PulseOne.SharedKernel AuthClaimValues). */
export const HOST_PORTAL_CLAIM = 'host';
export const PLATFORM_OPERATOR_ROLE = 'platform-operator';

/**
 * Host-portal auth facade. State is exposed as signals (CLAUDE.md: signals for state). MSAL owns
 * the token lifecycle (silent acquisition + refresh-token rotation handled by the IdP); this
 * service only surfaces account state and login/logout intents.
 *
 * SECURITY: `isHostOperator` here is for UI gating ONLY (show/hide nav, redirect). The
 * authoritative host boundary is the server-side HostOperatorsOnly policy (CLAUDE.md rule #4);
 * a forged claim buys nothing because every host API call is checked again on the server.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly msal = inject(MsalService);

  private readonly _account = signal<AccountInfo | null>(null);

  readonly account = this._account.asReadonly();
  readonly isAuthenticated = computed(() => this._account() !== null);

  private readonly claims = computed(
    () =>
      (this._account()?.idTokenClaims as Record<string, unknown> | undefined) ??
      undefined,
  );

  /** UI-only signal: does the signed-in principal look like a platform operator? */
  readonly isHostOperator = computed(() => {
    const c = this.claims();
    if (!c) {
      return false;
    }
    const portal = c['portal'] as string | undefined;
    const roles = c['roles'];
    const roleList = Array.isArray(roles)
      ? (roles as string[])
      : typeof roles === 'string'
        ? [roles]
        : [];
    return (
      portal === HOST_PORTAL_CLAIM && roleList.includes(PLATFORM_OPERATOR_ROLE)
    );
  });

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
    const cfg = getHostAuthConfig();
    const request: RedirectRequest = { scopes: [cfg.apiScope] };
    this.msal.loginRedirect(request);
  }

  logout(): void {
    this.msal.logoutRedirect();
  }

  private syncAccount(): void {
    const active =
      this.msal.instance.getActiveAccount() ??
      this.msal.instance.getAllAccounts()[0] ??
      null;
    if (active && !this.msal.instance.getActiveAccount()) {
      this.msal.instance.setActiveAccount(active);
    }
    this._account.set(active);
  }
}
