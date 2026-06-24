# Runbook: Zero-Downtime Secret Rotation (Key Vault + Managed Identity)

**Scorecard gate:** Secret rotation drill (blueprint §0).
**Owner:** Platform on-call.
**Frequency:** Quarterly, plus immediately on any suspected compromise.
**Pre-reqs:** `az` CLI logged in with the rotation principal; RBAC role `Key Vault Secrets Officer`
on the target vault; access to the Razorpay dashboard (for the webhook secret).

## Background

PulseOne reads all secrets from Azure Key Vault via Managed Identity — never from source
(CLAUDE.md security rule #1). At runtime the app binds them through `IOptionsMonitor<T>` /
`IConfiguration`, so a new secret version is picked up **without an app restart** once the
configuration reload interval elapses. Rotation is therefore additive: publish the new version,
let it propagate, then disable the old version.

Secrets in scope:
| Secret | Key Vault name | Consumer | Reload mechanism |
|---|---|---|---|
| Razorpay webhook secret | `razorpay-webhook-secret` | `IOptionsMonitor<RazorpayOptions>` | hot reload |
| Razorpay key secret | `razorpay-key-secret` | `IOptionsMonitor<RazorpayOptions>` | hot reload |
| SQL connection strings | `cs-tenant-catalog`, `cs-hangfire`, `cs-shard-*` | `IConfiguration` | reload on next scope |
| Redis connection string | `cs-redis` | `IConfiguration` | reload on next scope |

> Azure SQL and Redis here use **Managed Identity / AAD token auth** where possible; the connection
> "secret" is the host/database descriptor, not a password. Rotating the underlying identity is a
> separate AAD operation and does not require a string swap.

## Procedure

### 1. Publish the new secret version (old stays Enabled)
```bash
az keyvault secret set \
  --vault-name "$KV_NAME" \
  --name razorpay-webhook-secret \
  --value "<NEW_VALUE_FROM_RAZORPAY_DASHBOARD>"
```
The previous version remains **Enabled** so in-flight requests keep validating. Capture the old
version id for the rollback / disable steps:
```bash
OLD_VERSION=$(az keyvault secret list-versions --vault-name "$KV_NAME" --name razorpay-webhook-secret \
  --query "[?attributes.enabled].id | [1]" -o tsv)
```

### 2. Wait for propagation, then verify pickup (no restart)
The configuration provider polls Key Vault on its reload interval. Wait one interval, then confirm
the app is live and serving:
```bash
curl -fsS "https://$API_FQDN/health"   # expect HTTP 200
```
For the **Razorpay key id** (publishable, surfaced by `/api/v1/config/public`) confirm the new id is
served:
```bash
curl -fsS "https://$API_FQDN/api/v1/config/public"
```

### 3. Confirm the webhook accepts the new secret
From the Razorpay dashboard, send a **test event** to the production/staging webhook URL. Confirm in
App Insights that the event was received and signature-verified (look for the `WebhookOutcome.Verified`
trace). A `400` here means the app has **not** yet picked up the new secret — wait another reload
interval before disabling the old version. See `razorpay-verification.md` for the replay procedure.

### 4. Disable the old secret version
Only after step 3 succeeds with the NEW secret:
```bash
az keyvault secret set-attributes \
  --vault-name "$KV_NAME" \
  --name razorpay-webhook-secret \
  --version "$(basename "$OLD_VERSION")" \
  --enabled false
```

### 5. Record the rotation
- Append an entry to the audit log (rotated secret, date, operator, change ticket).
- Update the secret-rotation register in the team wiki with the next-due date.

## Rollback

If step 3 fails persistently or production webhooks start 400-ing after disabling the old version:
```bash
# Re-enable the previous version — propagation restores the prior behaviour within one reload interval.
az keyvault secret set-attributes \
  --vault-name "$KV_NAME" \
  --name razorpay-webhook-secret \
  --version "$(basename "$OLD_VERSION")" \
  --enabled true
```
If reload latency is unacceptable during an incident, force a fast refresh by restarting the API
revision (the worker is unaffected — it does not validate webhook signatures):
```bash
az containerapp revision restart --name pulseone-webapi --resource-group "$RG" \
  --revision "$(az containerapp revision list --name pulseone-webapi -g "$RG" \
    --query "[?properties.active].name | [0]" -o tsv)"
```

## Verification checklist
- [ ] New version Enabled in Key Vault; old version captured.
- [ ] `/health` returns 200 after propagation.
- [ ] Razorpay test event verified with the NEW secret (App Insights `Verified` trace).
- [ ] Old version Disabled.
- [ ] Rotation recorded in audit log + wiki.
