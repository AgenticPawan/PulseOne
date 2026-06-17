import { test, expect } from '@playwright/test';

// Placeholder smoke test so the Playwright suite runs in CI from Phase 0 onward.
// Real tenant-isolation, webhook, host-403, and axe-core specs land in Phase 7.
test.describe('PulseOne smoke', () => {
  test('home page responds', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/PulseOne/);
  });
});
