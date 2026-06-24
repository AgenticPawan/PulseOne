# Runbook: Razorpay Sandbox E2E Verification & Webhook Replay

**Scorecard gate:** Razorpay sandbox E2E (blueprint §0).
**Owner:** Payments on-call.
**Environment:** Staging only — uses the Razorpay **sandbox** (test mode). Never run against
production tenant data.
**Security properties being proven:**
- Constant-time HMAC signature check (`CryptographicOperations.FixedTimeEquals`).
- Fast-ack pattern: verify → dedup → enqueue → 200, no inline mutation.
- Idempotency: Redis `SETNX` dedup by `X-Razorpay-Event-Id` (7-day TTL) — duplicate delivery is a no-op.

## Background

The webhook endpoint is `POST /api/v1/billing/razorpay/webhook` (anonymous; authenticity is the HMAC,
not a session). It returns **200** on `Verified` AND on `Duplicate` (so Razorpay stops retrying), and
**400 only** on a genuine signature failure (a spoofed delivery we refuse to ack). The webhook secret
is sourced from `IOptionsMonitor<RazorpayOptions>`, Key Vault-backed.

## Procedure

### 1. Configure the sandbox webhook
In the Razorpay dashboard (Test mode) → Settings → Webhooks:
- URL: `https://staging.pulseone.io/api/v1/billing/razorpay/webhook`
- Secret: the value stored in Key Vault as `razorpay-webhook-secret` (staging vault).
- Active events: `payment.captured`, `payment.failed`, `subscription.charged`, `subscription.halted`.

### 2. Trigger a test payment
Open the staging tenant portal billing page → start a checkout → complete it with a Razorpay
**test card** (e.g. `4111 1111 1111 1111`, any future expiry/CVV). The SPA posts the checkout
callback to `POST /api/v1/billing/verify-payment`, which **re-verifies server-side** with the
Key Vault key secret — the browser's "success" is never trusted.

### 3. Verify single, correct processing
- In App Insights, find the request trace: expect `WebhookOutcome.Verified` and an enqueued Hangfire
  job (producer→queue→consumer correlation).
- Confirm the worker processed it **exactly once** and the subscription/payment row updated in the
  shard DB.

### 4. Force a duplicate delivery → prove idempotency
In the dashboard, open the webhook delivery log for the event and click **Resend** (same
`X-Razorpay-Event-Id`). Expected:
- Endpoint returns **200** (so Razorpay stops retrying).
- Trace shows `WebhookOutcome.Duplicate` — the Redis `SETNX` for that event id already exists, so the
  command short-circuits and **no second Hangfire job is enqueued** and **no double-apply** occurs.
- DB state is unchanged from step 3.

### 5. Negative test — forged signature is rejected
Send a hand-crafted request with a wrong signature (see replay snippet below with a bad secret).
Expected **400** and `WebhookOutcome.InvalidSignature`; nothing enqueued.

## Webhook replay (manual, for incident reproduction)

Recompute the HMAC the way the server does — over the **exact raw bytes** — using the staging secret.
This mirrors the verifier (HMAC-SHA256, hex digest, constant-time compare).
```bash
BODY='{"entity":"event","event":"payment.captured","payload":{"payment":{"entity":{"id":"pay_test_123"}}}}'
SECRET="<staging razorpay-webhook-secret>"          # pull from Key Vault; do NOT hardcode
SIG=$(printf '%s' "$BODY" | openssl dgst -sha256 -hmac "$SECRET" | sed 's/^.* //')

curl -i -X POST "https://staging.pulseone.io/api/v1/billing/razorpay/webhook" \
  -H "Content-Type: application/json" \
  -H "X-Razorpay-Signature: $SIG" \
  -H "X-Razorpay-Event-Id: evt_replay_$(date +%s)" \
  --data-raw "$BODY"            # --data-raw: do NOT let curl re-encode the body (signature is byte-exact)
```
Reuse the same `X-Razorpay-Event-Id` to reproduce the duplicate (200, no-op) path.

## Rollback / cleanup
- Delete or disable the sandbox webhook entry if it was created only for the drill.
- Test-mode data lives only in the staging shard; no production cleanup is required.
- If the drill enqueued stuck jobs, drain them via the Hangfire dashboard (staging) per the DLQ
  procedure from Phase 3.

## Verification checklist
- [ ] Test payment verified server-side via `/verify-payment` (browser result not trusted).
- [ ] First delivery: `Verified`, one job enqueued, DB updated once.
- [ ] Resend (same event id): `Duplicate`, 200, no second job, DB unchanged.
- [ ] Forged signature: 400, `InvalidSignature`, nothing enqueued.
