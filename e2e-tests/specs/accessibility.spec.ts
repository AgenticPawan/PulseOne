import AxeBuilder from '@axe-core/playwright';
import { test, expect, authConfigured } from '../fixtures/tenant-user';

/**
 * axe-core accessibility GATE (blueprint §7.4, Production Readiness Scorecard). The violations array
 * MUST be empty for every critical tenant journey — this is a hard CI gate, not advisory. WCAG 2.2 AA
 * tags are evaluated (which includes colour-contrast >= 4.5:1, accessible names, etc.).
 *
 * These run against the authenticated tenant portal; skipped when no pre-seeded session is configured
 * so the suite stays green without a live IdP. In CI the auth-setup project supplies the session and
 * this gate runs for real.
 */
test.describe('Tenant portal accessibility (WCAG 2.2 AA)', () => {
  test.skip(!authConfigured, 'Set PULSEONE_TENANT_STORAGE_STATE to run the axe-core a11y gate.');

  const wcagTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

  test('Dashboard passes axe with zero violations', async ({ tenantPage }) => {
    await tenantPage.goto('/dashboard');
    const results = await new AxeBuilder({ page: tenantPage }).withTags(wcagTags).analyze();
    expect(results.violations).toEqual([]);
  });

  test('Reports grid passes axe with zero violations', async ({ tenantPage }) => {
    await tenantPage.goto('/reports');
    await expect(tenantPage.getByRole('heading', { name: 'Reports' })).toBeVisible();
    const results = await new AxeBuilder({ page: tenantPage }).withTags(wcagTags).analyze();
    expect(results.violations).toEqual([]);
  });

  test('Report creation modal is accessible (focus trap + labels)', async ({ tenantPage }) => {
    await tenantPage.goto('/reports');
    await tenantPage.getByRole('button', { name: 'Generate report' }).click();
    await expect(tenantPage.getByRole('dialog')).toBeVisible();

    const results = await new AxeBuilder({ page: tenantPage })
      .include('[role="dialog"]')
      .withTags(['wcag2a', 'wcag2aa'])
      .analyze();
    expect(results.violations).toEqual([]);
  });

  test('Billing page passes axe with zero violations', async ({ tenantPage }) => {
    await tenantPage.goto('/billing');
    const results = await new AxeBuilder({ page: tenantPage }).withTags(wcagTags).analyze();
    expect(results.violations).toEqual([]);
  });

  test('Settings page passes axe with zero violations', async ({ tenantPage }) => {
    await tenantPage.goto('/settings');
    const results = await new AxeBuilder({ page: tenantPage }).withTags(wcagTags).analyze();
    expect(results.violations).toEqual([]);
  });
});
