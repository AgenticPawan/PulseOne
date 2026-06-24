import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import {
  HTTP_INTERCEPTORS,
  provideHttpClient,
  withFetch,
  withInterceptorsFromDi,
} from '@angular/common/http';
import {
  MsalBroadcastService,
  MsalGuard,
  MsalInterceptor,
  MsalService,
  MSAL_GUARD_CONFIG,
  MSAL_INSTANCE,
  MSAL_INTERCEPTOR_CONFIG,
} from '@azure/msal-angular';
import { routes } from './app.routes';
import {
  msalGuardConfigFactory,
  msalInstanceFactory,
  msalInterceptorConfigFactory,
} from './core/auth/auth.config';
import { AuthService } from './core/auth/auth.service';
import { TenantThemeService } from './core/services/tenant-theme.service';
import { ReportHubService } from './core/services/report-hub.service';

// Zoneless change detection is mandatory for the tenant portal (CLAUDE.md stack).
//
// MSAL wiring (Authorization Code + PKCE, public client — no secret):
//  - MSAL_INSTANCE / GUARD / INTERCEPTOR configs come from the runtime-injected auth config.
//  - MsalInterceptor is registered through DI (withInterceptorsFromDi) so it attaches the API
//    bearer token to /api/ calls only (protectedResourceMap), never to third-party origins.
//  - The app initializer runs the mandatory MSAL v4 instance.initialize() + handleRedirectPromise()
//    before the app renders, then wires the AuthService account signal.
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideHttpClient(withFetch(), withInterceptorsFromDi()),

    { provide: MSAL_INSTANCE, useFactory: msalInstanceFactory },
    { provide: MSAL_GUARD_CONFIG, useFactory: msalGuardConfigFactory },
    { provide: MSAL_INTERCEPTOR_CONFIG, useFactory: msalInterceptorConfigFactory },
    { provide: HTTP_INTERCEPTORS, useClass: MsalInterceptor, multi: true },
    MsalService,
    MsalGuard,
    MsalBroadcastService,

    provideAppInitializer(async () => {
      const msal = inject(MsalService);
      const auth = inject(AuthService);
      const theme = inject(TenantThemeService);
      const reportHub = inject(ReportHubService);

      await msal.instance.initialize();
      await msal.instance.handleRedirectPromise();
      auth.initialize();

      // Apply the server-injected per-subdomain theme (--tenant-primary / --tenant-accent).
      theme.apply();

      // Connect the SignalR ReportHub (prompt 01). The hub joins the connection to the tenant's own
      // group server-side. Connection failures are swallowed inside connect() so realtime being
      // unavailable never blocks app render — the reports grid degrades to its httpResource read.
      void reportHub.connect();
    }),
  ],
};
