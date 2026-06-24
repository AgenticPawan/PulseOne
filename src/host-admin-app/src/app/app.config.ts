import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
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

// The host portal is zoneless as well. The HostOperatorsOnly boundary is enforced SERVER-SIDE
// (CLAUDE.md security rule #4); the MSAL/host-operator guard wired here is UI-only.
//
// MSAL wiring mirrors the tenant portal but against the host-operator B2C flow: the interceptor
// attaches the host API bearer token to /api/ calls only, and the app initializer runs the
// mandatory MSAL v4 instance.initialize() + handleRedirectPromise() before render.
export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
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
      await msal.instance.initialize();
      await msal.instance.handleRedirectPromise();
      auth.initialize();
    }),
  ],
};
