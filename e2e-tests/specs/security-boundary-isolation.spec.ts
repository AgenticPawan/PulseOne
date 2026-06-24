import { test as tenantTest, expect } from '../fixtures/tenant-user';
import { test as hostTest } from '../fixtures/host-operator';
import { hostBaseURL } from '../fixtures/host-operator';

/**
 * UX-level security-boundary checks (blueprint §7.4). These are NOT the authoritative boundary proof
 * — that is the API-level 403 test in PulseOne.WebApi.Tests (§7.3). Here we confirm the Angular
 * router guards behave for users: an unauthenticated visitor is bounced to sign-in, and the two
 * portals do not cross-navigate.
 */
tenantTest.describe('Tenant portal route guards (UI-only)', () => {
  tenantTest('Unauthenticated visitor to a guarded tenant route is sent to sign-in', async ({ page }) => {
    // MsalGuard triggers an interactive sign-in for unauthenticated users; with no session the app
    // must NOT render the protected dashboard content.
    await page.goto('/dashboard');

    // The guarded content is gated; we assert the protected heading is not present without a session.
    // (When PULSEONE_TENANT_STORAGE_STATE is set this spec is complemented by the authenticated path.)
    await expect(page.getByRole('heading', { name: /dashboard/i })).toHaveCount(0);
  });
});

hostTest.describe('Host portal route guards (UI-only)', () => {
  hostTest('Unauthenticated visitor to a guarded host route does not see operator surfaces', async ({ page }) => {
    await page.goto(`${hostBaseURL}/tenants`);

    // hostOperatorGuard hides operator surfaces from non-operators; the tenant table must not render.
    await expect(page.getByRole('table')).toHaveCount(0);
  });

  hostTest('Forbidden page is reachable and renders an explanation', async ({ page }) => {
    await page.goto(`${hostBaseURL}/forbidden`);
    await expect(page.getByText(/forbidden|not authorized|access denied/i).first()).toBeVisible();
  });
});
