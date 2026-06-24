# Runbook: Region Failover Drill (Front Door + Azure SQL geo-replication)

**Scorecard gate:** Region failover drill (blueprint §0).
**Owner:** Platform on-call + DBA.
**Topology:** Primary = **West India** (`westindia`), Secondary = **East Asia** (`eastasia`)
(see `infra/main.bicep`: `location` / `secondaryLocation`).
**Targets:** **RPO ≤ 5 minutes**, **RTO ≤ 15 minutes**.
**Schedule:** Semi-annual scheduled drill in a maintenance window; this is also the live runbook for
a real regional outage.

## Background

- **Front Door Premium** fronts both regions. Each region's ACA ingress FQDN is registered as an
  origin in the origin group; Front Door health probes mark an unhealthy origin out of rotation and
  routes to the healthy region. WAF (OWASP, Prevention mode) sits in front of both.
- **Azure SQL active geo-replication** keeps a continuously-synced readable secondary in East Asia
  for the tenant-catalog, hangfire, and shard databases. Failover **promotes** the secondary to
  primary (read-write).
- The app is stateless (producer) + queue-driven (worker); no session affinity to lose.

## Drill / Failover Procedure

### 0. Pre-checks (record a clean baseline)
```bash
# Confirm geo-replication is seeding and measure current data lag (this is your pre-failover RPO).
az sql db replica list-links --name "$DB" --server "$PRIMARY_SQL" --resource-group "$RG" \
  --query "[].{role:role, lag:replicationLag, state:replicationState}" -o table
```

### 1. Simulate primary failure (disable the primary origin in Front Door)
```bash
az afd origin update \
  --resource-group "$RG" \
  --profile-name "$AFD_PROFILE" \
  --origin-group-name pulseone-origins \
  --origin-name primary-westindia \
  --enabled-state Disabled
```
Front Door stops routing to the primary. (In a real outage Front Door health probes do this
automatically; disabling the origin makes the drill deterministic.)

### 2. Verify Front Door routes to the secondary (target < 60s)
```bash
# Repeatedly hit the edge; confirm 200s continue to be served (now from East Asia).
for i in $(seq 1 12); do curl -s -o /dev/null -w '%{http_code}\n' "https://app.pulseone.io/health"; sleep 5; done
```
Confirm in Front Door metrics that requests are now landing on the `secondary-eastasia` origin.

### 3. Promote the SQL secondary (planned vs forced)
**Planned failover** (drill / graceful — no data loss, syncs first):
```bash
az sql db replica set-primary --name "$DB" --server "$SECONDARY_SQL" --resource-group "$RG"
```
**Forced failover** (real outage, primary unreachable — may lose up to the replication lag):
```bash
az sql db replica set-primary --name "$DB" --server "$SECONDARY_SQL" --resource-group "$RG" --allow-data-loss
```
Repeat for each database: tenant-catalog, hangfire, and every active shard.

### 4. Point the secondary-region app at the promoted primary
The East Asia ACA apps read their SQL connection strings from the **regional Key Vault** secrets.
Confirm those point at the (now-primary) East Asia SQL endpoint. If they were pinned to the West
India endpoint, update the Key Vault secret (see `secret-rotation.md`) — the app picks it up on the
next configuration reload; restart the revision if you need it immediately.

### 5. Measure RPO and RTO
- **RPO:** the replication lag recorded in step 0 at the moment of failover (planned failover RPO = 0).
- **RTO:** wall-clock from step 1 (failure injected) to the first sustained 200s in step 2 **plus** a
  successful authenticated write (e.g. create a report) confirming read-write on the promoted primary.
- Record both against the ≤ 5 min / ≤ 15 min targets. File a corrective action if either is exceeded.

## Failback (return to West India)

1. Re-enable the primary origin in Front Door **only after** West India SQL is healthy again.
2. Re-establish geo-replication West India ← East Asia and let it fully seed.
3. Planned-failover SQL back to West India (`set-primary` WITHOUT `--allow-data-loss`).
4. Verify Front Door is serving from West India and the East Asia secondary is read-only again.

## Rollback (abort the drill before promotion)
If the drill must be aborted **before** step 3 (no SQL promotion yet), simply re-enable the primary
origin — no data operation occurred:
```bash
az afd origin update --resource-group "$RG" --profile-name "$AFD_PROFILE" \
  --origin-group-name pulseone-origins --origin-name primary-westindia --enabled-state Enabled
```

## Verification checklist
- [ ] Baseline replication lag recorded (pre-failover RPO).
- [ ] Front Door served continuous 200s within 60s of origin disable.
- [ ] SQL secondary promoted for catalog + hangfire + all shards.
- [ ] Authenticated write succeeds against the promoted primary.
- [ ] RPO ≤ 5 min and RTO ≤ 15 min recorded; deviations have corrective actions.
- [ ] Failback completed and geo-replication re-seeded.
