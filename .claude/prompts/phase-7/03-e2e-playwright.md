# Prompt: Playwright E2E + Accessibility Tests

## Context
Blueprint §7.4 notes that Playwright tests verify UX behaviour — they are NOT the authoritative boundary proof (that's the API-level 403 test in §7.3). E2E tests confirm the golden paths and accessibility. axe-core must run in CI as a gate.

## Task

### Test Structure (`e2e-tests/specs/`)
```
e2e-tests/
├── playwright.config.ts
├── fixtures/
│   ├── tenant-user.ts        # authenticated tenant user fixture
│   └── host-operator.ts      # authenticated host operator fixture
└── specs/
    ├── tenant-portal.spec.ts
    ├── host-portal.spec.ts
    ├── security-boundary-isolation.spec.ts
    └── accessibility.spec.ts
```

### `playwright.config.ts`
```typescript
export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: [['html'], ['junit', { outputFile: 'results.xml' }]],
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:4200',
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox',  use: { ...devices['Desktop Firefox'] } },
  ],
});
```

### Security Boundary Tests (§7.4)
```typescript
test('Tenant is redirected away from host UI', async ({ page }) => {
  await page.goto('/host');
  await expect(page).toHaveURL(/.*\/auth\/login/);
});

test('Host operator cannot access tenant-specific routes', async ({ hostPage }) => {
  await hostPage.goto('/app/tenant-dashboard');
  await expect(hostPage).toHaveURL(/.*\/host\//);  // redirected to host home
});
```

### Tenant Portal Golden Path
```typescript
test('Tenant can create and download a report', async ({ tenantPage }) => {
  await tenantPage.goto('/app/reports');
  await tenantPage.getByRole('button', { name: 'New Report' }).click();
  await tenantPage.getByLabel('Report Name').fill('Q2 Sales Summary');
  await tenantPage.getByRole('button', { name: 'Generate' }).click();
  await expect(tenantPage.getByRole('status')).toContainText('Report queued');
  // Wait for SignalR notification (up to 30s)
  await expect(tenantPage.getByText('Q2 Sales Summary').first()).toBeVisible({ timeout: 30000 });
});
```

### Accessibility Tests (axe-core)
```typescript
import AxeBuilder from '@axe-core/playwright';

test('Tenant portal home page passes axe WCAG 2.2 AA', async ({ page }) => {
  await page.goto('/app/dashboard');
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag22aa'])
    .analyze();
  expect(results.violations).toEqual([]);
});

test('Report creation modal is accessible', async ({ page }) => {
  await page.goto('/app/reports');
  await page.getByRole('button', { name: 'New Report' }).click();
  const results = await new AxeBuilder({ page })
    .include('[role="dialog"]')
    .withTags(['wcag2a', 'wcag2aa'])
    .analyze();
  expect(results.violations).toEqual([]);
});
```

### Critical Accessibility Checks
- Skip links present and functional
- All interactive elements have accessible names
- Focus visible on keyboard navigation
- Color contrast ≥ 4.5:1 (axe-core checks this automatically)
- Modal focus trap: Tab/Shift+Tab stays within modal

## Output Location
`e2e-tests/`

## Constraints
- axe-core violations array must be empty for all critical journeys — this is a CI gate per the Production Readiness Scorecard
- Tests must run in headed and headless mode
- Authentication fixtures use pre-seeded test users (not live Azure AD)
- Never hardcode test credentials — use environment variables
