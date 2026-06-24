import { test as base, type Page } from '@playwright/test';

/**
 * Authenticated tenant-user fixture (blueprint §7.4). The tenant portal authenticates via MSAL
 * against Azure AD B2C; E2E runs against a PRE-SEEDED test tenant user, never a live interactive
 * sign-in. Credentials/state come from the environment — NEVER hardcoded (prompt constraint).
 *
 * Two wiring modes are supported:
 *  1. PULSEONE_TENANT_STORAGE_STATE — path to a Playwright storageState JSON captured once by an
 *     auth-setup project (the recommended CI approach: sign in once, reuse the cookies/tokens).
 *  2. Fallback — an unauthenticated page; specs that require auth should be guarded by
 *     `test.skip(!authConfigured, ...)` so the suite stays green when no test IdP is wired up.
 */
const storageStatePath = process.env.PULSEONE_TENANT_STORAGE_STATE;

export const authConfigured = Boolean(storageStatePath);

type TenantFixtures = {
  tenantPage: Page;
};

export const test = base.extend<TenantFixtures>({
  tenantPage: async ({ browser }, use) => {
    const context = await browser.newContext(
      storageStatePath ? { storageState: storageStatePath } : {},
    );
    const page = await context.newPage();
    await use(page);
    await context.close();
  },
});

export { expect } from '@playwright/test';
