import { test, expect, hostAuthConfigured } from '../fixtures/host-operator';

/**
 * Host admin portal golden paths (blueprint §7.4). Confirms an authenticated platform operator can
 * reach the operator surfaces (tenants, subscriptions, audit, health). Requires a pre-seeded
 * operator session (PULSEONE_HOST_STORAGE_STATE) and a running host API; skipped otherwise.
 *
 * NOTE: passing here only proves the UI renders for an operator. That an operator is REQUIRED is
 * proven authoritatively at the API layer (PulseOne.WebApi.Tests host-boundary suite).
 */
test.describe('Host portal — operator golden path', () => {
  test.skip(!hostAuthConfigured, 'Set PULSEONE_HOST_STORAGE_STATE to run authenticated host E2E.');

  test('Operator can open the tenant list', async ({ hostPage }) => {
    await hostPage.goto('/tenants');
    await expect(hostPage.getByRole('heading', { name: /tenants/i })).toBeVisible();
    await expect(hostPage.getByRole('table')).toBeVisible();
  });

  test('Operator can open the subscription dashboard', async ({ hostPage }) => {
    await hostPage.goto('/subscriptions');
    await expect(hostPage.getByRole('heading', { name: /subscription/i })).toBeVisible();
  });

  test('Operator can open the cross-tenant audit browser', async ({ hostPage }) => {
    await hostPage.goto('/audit');
    await expect(hostPage.getByRole('heading', { name: /audit/i })).toBeVisible();
  });

  test('Operator can open system health', async ({ hostPage }) => {
    await hostPage.goto('/health');
    await expect(hostPage.getByRole('heading', { name: /health|system/i })).toBeVisible();
  });
});
