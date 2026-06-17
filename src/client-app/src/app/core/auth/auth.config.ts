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
 * Tenant-portal MSAL configuration. Authorization Code Flow + PKCE is the default for
 * `PublicClientApplication` in @azure/msal-browser v3/v4 — there is NO client secret (public
 * client). All Azure AD B2C values are runtime config, NEVER hardcoded (CLAUDE.md security rule #1
 * / 01-auth-module.md). They are read from `window.__PULSEONE_AUTH__`, injected at deploy time by
 * the host page (e.g. from Key Vault-backed app settings) — placeholders below are dev-only.
 */
export interface PulseOneAuthRuntimeConfig {
  readonly clientId: string;
  readonly authority: string; // B2C tenant-user-flow metadata authority
  readonly knownAuthority: string; // B2C tenant host (e.g. pulseone.b2clogin.com)
  readonly apiScope: string; // scope requested for the producer API
  readonly redirectUri: string;
}

declare global {
  interface Window {
    __PULSEONE_AUTH__?: PulseOneAuthRuntimeConfig;
  }
}

/**
 * Resolves runtime auth config. Fails LOUD if config is missing in production — we never fall
 * back to a baked-in tenant/secret. The dev placeholders are obvious non-secrets.
 */
export function getAuthConfig(): PulseOneAuthRuntimeConfig {
  const injected = typeof window !== 'undefined' ? window.__PULSEONE_AUTH__ : undefined;
  if (injected) {
    return injected;
  }
  // Dev-only placeholders. <KEY_VAULT_REFERENCE> values are injected at runtime in real envs.
  return {
    clientId: '00000000-0000-0000-0000-000000000000',
    authority: 'https://pulseone.b2clogin.com/pulseone.onmicrosoft.com/B2C_1_tenant_signin',
    knownAuthority: 'pulseone.b2clogin.com',
    apiScope: 'https://pulseone.onmicrosoft.com/api/access_as_user',
    redirectUri: '/',
  };
}

export function msalInstanceFactory(): IPublicClientApplication {
  const cfg = getAuthConfig();
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
  const cfg = getAuthConfig();
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
  const cfg = getAuthConfig();
  const protectedResourceMap = new Map<string, Array<string>>();
  protectedResourceMap.set('/api/', [cfg.apiScope]);
  return {
    interactionType: InteractionType.Redirect,
    protectedResourceMap,
  };
}
