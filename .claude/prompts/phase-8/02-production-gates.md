# Prompt: Production Readiness Gates — Checklists & Drills

## Context
The Production Readiness Scorecard (blueprint §0) lists gates that turn a "design 10/10" into a "production-vetted" system. This prompt covers the documentation, scripts, and runbooks for closing those gates.

## Task

### Load Test Script (`tests/load/k6-load-test.js`)
Use k6 to simulate realistic load:
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '2m', target: 50 },    // ramp up
    { duration: '5m', target: 200 },   // sustained load
    { duration: '2m', target: 0 },     // ramp down
  ],
  thresholds: {
    http_req_duration: ['p99<500'],    // p99 latency < 500ms
    http_req_failed:   ['rate<0.01'],  // error rate < 1%
  },
};

export default function () {
  // Tenant API: list reports (authenticated)
  const res = http.get(`${__ENV.BASE_URL}/api/v1/reports`, {
    headers: { Authorization: `Bearer ${__ENV.TEST_TOKEN}` },
  });
  check(res, { 'status 200': (r) => r.status === 200 });
  sleep(1);
}
```

### Secret Rotation Runbook (`docs/runbooks/secret-rotation.md`)
Document the zero-downtime rotation procedure:
1. Add new version of secret in Key Vault (old version remains active)
2. Verify `IOptionsMonitor` picks up new value without restart (test endpoint: `GET /health/ready`)
3. Confirm Razorpay webhook is accepted with new secret by sending a test event from Razorpay dashboard
4. Disable old Key Vault secret version
5. Record rotation date in audit log

### Region Failover Drill (`docs/runbooks/region-failover.md`)
Document Azure Front Door + SQL geo-replication failover:
1. Simulate primary region failure (disable origin in Front Door)
2. Verify Front Door routes to secondary region (< 60s)
3. Verify Azure SQL active geo-replication promoted secondary to primary
4. Verify RPO: measure data lag between primary and secondary at time of failover
5. Verify RTO: measure time from failure to full recovery
6. Target: RPO ≤ 5 min, RTO ≤ 15 min

### Razorpay Sandbox E2E Verification (`docs/runbooks/razorpay-verification.md`)
1. Configure sandbox webhook in Razorpay dashboard pointing to staging
2. Trigger a test payment via sandbox checkout
3. Verify webhook received, signature verified, event processed exactly once
4. Force duplicate delivery from Razorpay dashboard → verify idempotency (no double-apply)
5. Verify subscription status updated in DB

### Third-Party Pen Test Brief (`docs/security/pentest-brief.md`)
Scope document for external penetration testers covering:
- Multi-tenant data isolation (attempt cross-tenant data access)
- Payment webhook forgery (attempt to forge Razorpay webhooks)
- JWT/session attacks (token replay, tenant claim manipulation)
- Host portal access from tenant credentials
- Rate limiting effectiveness
- OIDC/PKCE implementation correctness

## Output Locations
- `tests/load/k6-load-test.js`
- `docs/runbooks/secret-rotation.md`
- `docs/runbooks/region-failover.md`
- `docs/runbooks/razorpay-verification.md`
- `docs/security/pentest-brief.md`

## Constraints
- Load test must validate KEDA scale-out is triggered and workers spin up during the load phase
- All runbooks must include rollback steps
- Pentest brief must explicitly exclude production tenant data from scope
