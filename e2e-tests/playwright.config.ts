import { defineConfig, devices } from '@playwright/test';

// Playwright config for PulseOne E2E (Phase 7). Specs live in ./specs.
//
// Two front-ends are exercised: the tenant portal (client-app, default :4200) and the host admin
// portal (host-admin-app, default :4300). Base URLs are environment-driven so CI can target a
// deployed preview slot; locally they default to the Angular dev-server ports. The host portal URL
// is read inside host specs via PULSEONE_HOST_BASE_URL.
//
// axe-core accessibility specs are a CI GATE (Production Readiness Scorecard): the violations array
// must be empty for every critical journey.
export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  // junit output feeds the CI test-summary gate; html for local triage; github annotations in CI.
  reporter: process.env.CI
    ? [['html'], ['junit', { outputFile: 'results.xml' }], ['github']]
    : 'list',
  use: {
    baseURL: process.env.PULSEONE_BASE_URL ?? 'http://localhost:4200',
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
  ],
});
