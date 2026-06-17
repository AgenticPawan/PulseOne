# Prompt: Angular Billing Service — Runtime Config, No Hardcoded Keys

## Context
Blueprint §6.5 corrects two v1 defects in the Angular billing code:
1. Frontend Razorpay key was hardcoded as `rzp_test_...` in source
2. HTTP was driven by `effect()` — causing overlapping, un-cancelled requests on signal changes

## Task

### RazorpayBillingService (blueprint §6.5 — implement exactly)
- Load Razorpay checkout.js script dynamically (only once)
- Fetch publishable key ID from `/api/v1/config/public` at checkout time (not at service init)
- Open checkout with key from server response
- Post payment result to `/api/v1/billing/verify-payment` for server-side verification

### Billing Feature Module (`src/client-app/src/app/features/billing/`)

**`billing-plans.component.ts`** — displays available subscription plans
- Fetches plans via `httpResource<Plan[]>(() => ({ url: '/api/v1/billing/plans' }))`
- Renders plan cards with price, features, current tier highlighted
- "Upgrade" button calls `RazorpayBillingService.initiateCheckout()`

**`billing-history.component.ts`** — paged list of past payments
- Uses `httpResource` with `pageIndex` and `dateRange` signals
- Columns: Date, Amount, Status, Invoice (download link)

**`subscription-status.component.ts`** — current plan widget
- Shows tier name, renewal date, usage meters
- Reads from `httpResource<SubscriptionStatus>(() => '/api/v1/billing/subscription')`

### Subscription Confirmation (after `verifyOnBackend`)
After successful payment verification:
1. Emit a `paymentSuccess` event from `RazorpayBillingService`
2. `BillingPlansComponent` listens and refreshes `httpResource` automatically via signal change
3. Show a success toast notification

### SCSS / Theming
- Razorpay checkout button uses `--tenant-accent` CSS variable for background color
- This is already wired in `initiateCheckout`: `theme.color = getComputedStyle(document.documentElement).getPropertyValue('--tenant-accent').trim()`

## Output Locations
- `src/client-app/src/app/core/services/razorpay-billing.service.ts`
- `src/client-app/src/app/features/billing/`

## Constraints
- `rzp_test_` or `rzp_live_` strings must NEVER appear in source code
- Use `httpResource` for all reactive data fetching — NOT `effect()` + subscribe
- `verifyOnBackend` must be server-authoritative — client result from Razorpay is UNTRUSTED until backend verifies the payment signature
- Angular `DomSanitizer` must be used if any HTML from the API is rendered
