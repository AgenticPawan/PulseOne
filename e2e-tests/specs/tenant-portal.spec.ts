import { test, expect, authConfigured } from '../fixtures/tenant-user';

/**
 * Tenant portal golden paths (blueprint §7.4). Exercises the report create -> queue -> SignalR
 * completion flow against the real client-app. Requires a pre-seeded authenticated session
 * (PULSEONE_TENANT_STORAGE_STATE) and a running tenant API; skipped otherwise so the suite stays
 * green in environments without a live IdP/backend.
 */
test.describe('Tenant portal — reports golden path', () => {
  test.skip(!authConfigured, 'Set PULSEONE_TENANT_STORAGE_STATE to run authenticated tenant E2E.');

  test('Tenant can open the report grid', async ({ tenantPage }) => {
    await tenantPage.goto('/reports');
    await expect(tenantPage.getByRole('heading', { name: 'Reports' })).toBeVisible();
    await expect(tenantPage.getByRole('button', { name: 'Generate report' })).toBeVisible();
  });

  test('Tenant can create a report and see it queued', async ({ tenantPage }) => {
    await tenantPage.goto('/reports');
    await tenantPage.getByRole('button', { name: 'Generate report' }).click();

    // The create dialog (report-create.component) collects a name + type.
    const dialog = tenantPage.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByLabel(/report name/i).fill('Q2 Sales Summary');
    await dialog.getByRole('button', { name: /generate|create/i }).click();

    // Fast-ack: the grid announces the queued report via an aria-live status region.
    await expect(tenantPage.getByRole('status')).toContainText(/queued|generating|processing/i);

    // SignalR-driven completion (up to 30s) refreshes the grid with the new row.
    await expect(tenantPage.getByText('Q2 Sales Summary').first()).toBeVisible({ timeout: 30_000 });
  });
});
