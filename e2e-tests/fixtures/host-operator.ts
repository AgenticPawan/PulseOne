import { test as base, type Page } from '@playwright/test';

/**
 * Authenticated host-operator fixture (blueprint §7.4). The host admin portal lives on its own
 * origin (PULSEONE_HOST_BASE_URL, default :4300). E2E uses a PRE-SEEDED platform-operator test user;
 * credentials/state come from the environment — NEVER hardcoded.
 *
 * Note: this fixture proves UX behaviour only. The AUTHORITATIVE host boundary is the server-side
 * `HostOperatorsOnly` policy proven by the API-level 403 test (PulseOne.WebApi.Tests, §7.3). The
 * router guard exercised here is UI-only (CLAUDE.md security rule #4).
 */
export const hostBaseURL = process.env.PULSEONE_HOST_BASE_URL ?? 'http://localhost:4300';

const storageStatePath = process.env.PULSEONE_HOST_STORAGE_STATE;

export const hostAuthConfigured = Boolean(storageStatePath);

type HostFixtures = {
  hostPage: Page;
};

export const test = base.extend<HostFixtures>({
  hostPage: async ({ browser }, use) => {
    const context = await browser.newContext({
      baseURL: hostBaseURL,
      ...(storageStatePath ? { storageState: storageStatePath } : {}),
    });
    const page = await context.newPage();
    await use(page);
    await context.close();
  },
});

export { expect } from '@playwright/test';
