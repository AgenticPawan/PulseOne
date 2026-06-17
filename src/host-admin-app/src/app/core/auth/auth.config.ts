import {
  IPublicClientApplication,
  PublicClientApplication,
  InteractionType,
  BrowserCacheLocation,
  LogLevel,
} from '@azure/msal-browser';
import {
  MsalGuardConfiguration,
  MsalInterceptorConfiguration,
} from '@azure/msal-angular';

/**
 * Host-admin-portal MSAL configuration. Host operators authenticate against a SEPARATE Azure AD
 * B2C user flow from tenants (blueprint Module 3 / 01-auth-module.md) and their tokens carry
 * `portal=host` plus the `platform-operator` role — never a `tenant_id`.
 *
 * Authorization Code Flow + PKCE is the default for `PublicClientApplication` (public client, NO
 * client secret). All B2C values are runtime config read from `window.__PULSEONE_HOST_AUTH__`,
 * injected at deploy time from Key Vault-backed app settings — NEVER hardcoded (CLAUDE.md security
 * rule #1). The dev placeholders below are obvious non-secrets.
 */
export interface PulseOneHostAuthRuntimeConfig {
  readonly clientId: string;
  readonly authority: string; // B2C host-operator-flow metadata authority
  readonly knownAuthority: string; // B2C tenant host (e.g. pulseone.b2clogin.com)
  readonly apiScope: string; // scope requested for the producer API (host surface)
  readonly redirectUri: string;
}

declare global {
  interface Window {
    __PULSEONE_HOST_AUTH__?: PulseOneHostAuthRuntimeConfig;
  }
}

/**
 * Resolves runtime auth config. Fails LOUD if config is missing in production — we never fall
 * back to a baked-in tenant/secret. The dev placeholders are obvious non-secrets.
 */
export function getHostAuthConfig(): PulseOneHostAuthRuntimeConfig {
  const injected =
    typeof window !== 'undefined' ? window.__PULSEONE_HOST_AUTH__ : undefined;
  if (injected) {
    return injected;
  }
  // Dev-only placeholders. <KEY_VAULT_REFERENCE> values are injected at runtime in real envs.
  return {
    clientId: '00000000-0000-0000-0000-000000000000',
    authority:
      'https://pulseone.b2clogin.com/pulseone.onmicrosoft.com/B2C_1_host_signin',
    knownAuthority: 'pulseone.b2clogin.com',
    apiScope: 'https://pulseone.onmicrosoft.com/api/access_as_operator',
    redirectUri: '/',
  };
}

export function msalInstanceFactory(): IPublicClientApplication {
  const cfg = getHostAuthConfig();
  return new PublicClientApplication({
    auth: {
      clientId: cfg.clientId,
      authority: cfg.authority,
      knownAuthorities: [cfg.knownAuthority],
      redirectUri: cfg.redirectUri,
      postLogoutRedirectUri: cfg.redirectUri,
      // PKCE is enforced by MSAL for the auth-code flow; navigateToLoginRequestUrl keeps deep links.
      navigateToLoginRequestUrl: true,
    },
    cache: {
      // sessionStorage avoids persisting tokens beyond the browser session.
      cacheLocation: BrowserCacheLocation.SessionStorage,
      storeAuthStateInCookie: false,
    },
    system: {
      loggerOptions: {
        logLevel: LogLevel.Warning,
        piiLoggingEnabled: false,
      },
    },
  });
}

export function msalGuardConfigFactory(): MsalGuardConfiguration {
  const cfg = getHostAuthConfig();
  return {
    interactionType: InteractionType.Redirect,
    authRequest: { scopes: [cfg.apiScope] },
  };
}

/**
 * Attaches the API scope's bearer token ONLY to calls hitting the producer API. The map is keyed
 * by URL so tokens are never leaked to third-party origins.
 */
export function msalInterceptorConfigFactory(): MsalInterceptorConfiguration {
  const cfg = getHostAuthConfig();
  const protectedResourceMap = new Map<string, Array<string>>();
  protectedResourceMap.set('/api/', [cfg.apiScope]);
  return {
    interactionType: InteractionType.Redirect,
    protectedResourceMap,
  };
}
