import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * UI-only route guard for the host admin portal. It triggers an interactive sign-in for anonymous
 * users and hides operator surfaces from principals lacking `portal=host` + `platform-operator`.
 *
 * THIS IS NOT A SECURITY BOUNDARY (CLAUDE.md security rule #4). The authoritative gate is the
 * server-side HostOperatorsOnly policy on the API — this guard only improves UX by avoiding dead
 * screens. Never move an authorization decision here that the server does not also enforce.
 */
export const hostOperatorGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isAuthenticated()) {
    auth.login();
    return false;
  }

  if (!auth.isHostOperator()) {
    // Authenticated but not a platform operator — bounce to an access-denied surface.
    return router.parseUrl('/forbidden');
  }

  return true;
};
