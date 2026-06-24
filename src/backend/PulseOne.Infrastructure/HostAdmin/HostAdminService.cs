using Microsoft.EntityFrameworkCore;
using PulseOne.Application.Features.HostAdmin;
using PulseOne.CoreDomain.Entities;
using PulseOne.Infrastructure.Persistence;
using PulseOne.Infrastructure.Persistence.Catalog;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.MultiTenancy;
using PulseOne.SharedKernel.Paging;

namespace PulseOne.Infrastructure.HostAdmin;

/// <summary>
/// Default <see cref="IHostAdminService"/>. Reads the Tenant Catalog directly (the host has full
/// catalog visibility — there is no tenant filter on the catalog) and builds host-scoped
/// business-shard contexts on demand for per-tenant and cross-tenant data.
/// </summary>
public sealed class HostAdminService(
    TenantCatalogDbContext catalog,
    ITenantCatalog catalogCache,
    ICurrentUser currentUser) : IHostAdminService
{
    // Carried into the FixedTenantContext for cross-tenant reads; never evaluated because those
    // queries bypass the "Tenant" named filter, so the value below is intentionally a non-tenant.
    private const string HostScopeSentinel = "__host__";

    // ---- Module 1: tenant lifecycle ----------------------------------------------------------

    public async Task<PagedResult<TenantSummaryDto>> ListTenantsAsync(
        TenantListQuery query, CancellationToken ct = default)
    {
        var q = catalog.TenantShards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            q = q.Where(t => t.Name.Contains(term) || t.TenantId.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<TenantStatus>(query.Status, ignoreCase: true, out var status))
        {
            q = q.Where(t => t.Status == status);
        }

        var descending = string.Equals(query.SortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        q = (query.SortColumn?.ToLowerInvariant()) switch
        {
            "plan" => descending ? q.OrderByDescending(t => t.Tier) : q.OrderBy(t => t.Tier),
            "status" => descending ? q.OrderByDescending(t => t.Status) : q.OrderBy(t => t.Status),
            "createdatutc" => descending ? q.OrderByDescending(t => t.CreatedAt) : q.OrderBy(t => t.CreatedAt),
            _ => descending ? q.OrderByDescending(t => t.Name) : q.OrderBy(t => t.Name),
        };

        var total = await q.CountAsync(ct);
        var rows = await q
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<TenantSummaryDto>(
            rows.Select(ToSummary).ToList(), total, query.PageNumber, query.PageSize);
    }

    public async Task<TenantDetailDto?> GetTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var t = await catalog.TenantShards.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        return t is null ? null : ToDetail(t);
    }

    public async Task<TenantDetailDto> ProvisionTenantAsync(
        ProvisionTenantRequest request, CancellationToken ct = default)
    {
        if (await catalog.TenantShards.AnyAsync(t => t.TenantId == request.TenantId, ct))
            throw new InvalidOperationException($"Tenant '{request.TenantId}' already exists.");

        // Shards are shared across tenants, so a new tenant on an existing shard label reuses that
        // shard's connection string. The localdb fallback mirrors the catalog seed for dev only —
        // production resolves shard connection strings from the shard registry / Key Vault.
        var existingOnShard = await catalog.TenantShards.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ShardLabel == request.AssignedShard, ct);
        var connectionString = existingOnShard?.ShardConnectionString
            ?? $"Server=(localdb)\\mssqllocaldb;Database=PulseOne_{request.AssignedShard};Trusted_Connection=True;";

        var tier = Enum.TryParse<TenantTier>(request.PlanTier, ignoreCase: true, out var parsed)
            ? parsed
            : TenantTier.Free;

        var shard = new TenantShard
        {
            TenantId = request.TenantId,
            Name = request.CompanyName,
            AdminEmail = request.AdminEmail,
            ShardConnectionString = connectionString,
            ShardLabel = request.AssignedShard,
            Region = "westindia", // ProvisionTenantRequest carries no region; default the seed region.
            Tier = tier,
            Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };

        catalog.TenantShards.Add(shard);
        await catalog.SaveChangesAsync(ct);
        await catalogCache.InvalidateAsync(request.TenantId, ct);

        return ToDetail(shard);
    }

    public async Task<bool> SuspendTenantAsync(string tenantId, CancellationToken ct = default) =>
        await SetLifecycleAsync(tenantId, TenantStatus.Suspended, isActive: false, ct);

    public async Task<bool> ReactivateTenantAsync(string tenantId, CancellationToken ct = default) =>
        await SetLifecycleAsync(tenantId, TenantStatus.Active, isActive: true, ct);

    private async Task<bool> SetLifecycleAsync(
        string tenantId, TenantStatus status, bool isActive, CancellationToken ct)
    {
        var shard = await catalog.TenantShards.FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (shard is null)
            return false;

        shard.Status = status;
        shard.IsActive = isActive; // routing flag the tenant-resolution pipeline reads.
        await catalog.SaveChangesAsync(ct);
        await catalogCache.InvalidateAsync(tenantId, ct);
        return true;
    }

    public async Task<IReadOnlyList<TenantUserSummaryDto>> GetTenantUsersAsync(
        string tenantId, CancellationToken ct = default)
    {
        var shard = await RequireShardAsync(tenantId, ct);
        await using var db = BuildContext(shard.ShardConnectionString, new FixedTenantContext(tenantId));

        // Role assignments + role name come from the shard; email/last-login live in the IdP and are
        // not mirrored here, so they surface as the user id / null until an IdP lookup is wired in.
        var grants = await db.TenantUserRoles.AsNoTracking()
            .Join(db.TenantRoles.AsNoTracking(),
                ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, RoleName = r.Name })
            .ToListAsync(ct);

        return grants
            .GroupBy(g => g.UserId)
            .Select(g => new TenantUserSummaryDto(
                g.Key, g.Key, string.Join(", ", g.Select(x => x.RoleName).Distinct()), null))
            .OrderBy(u => u.UserId)
            .ToList();
    }

    public async Task<TenantStorageUsageDto> GetTenantStorageAsync(
        string tenantId, CancellationToken ct = default)
    {
        var shard = await RequireShardAsync(tenantId, ct);
        await using var db = BuildContext(shard.ShardConnectionString, new FixedTenantContext(tenantId));

        var documentCount = await db.Reports.AsNoTracking().CountAsync(ct);
        // UsedBytes has no metered source in the shard (blob usage is reported by Azure Storage);
        // surfaced as 0 until that metering feed is wired in. Quota is derived from the tier.
        return new TenantStorageUsageDto(UsedBytes: 0, QuotaBytes: QuotaBytes(shard.Tier), documentCount);
    }

    public async Task<IReadOnlyList<TenantSubscriptionHistoryEntryDto>> GetTenantSubscriptionsAsync(
        string tenantId, CancellationToken ct = default)
    {
        var shard = await RequireShardAsync(tenantId, ct);
        await using var db = BuildContext(shard.ShardConnectionString, new FixedTenantContext(tenantId));

        // The "Tenant" filter is bound to tenantId here, so this returns only this tenant's rows.
        var subs = await db.Subscriptions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return subs
            .Select(s => new TenantSubscriptionHistoryEntryDto(
                s.RazorpaySubscriptionId, s.PlanId, s.Status, s.ActivatedAt ?? s.CreatedAt, s.CancelledAt))
            .ToList();
    }

    public async Task<PagedResult<AuditLogEntryDto>> GetTenantAuditAsync(
        string tenantId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var shard = await RequireShardAsync(tenantId, ct);
        await using var db = BuildContext(shard.ShardConnectionString, new FixedTenantContext(tenantId));

        // AuditLog is not multi-tenant-filtered (it is excluded so audit writes never recurse), and a
        // shard can host several tenants, so filter by tenant id explicitly.
        var q = db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.Timestamp);

        var total = await q.CountAsync(ct);
        var rows = await q.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditLogEntryDto>(
            rows.Select(ToAuditDto).ToList(), total, pageNumber, pageSize);
    }

    // ---- Module 2: subscriptions -------------------------------------------------------------

    public async Task<PagedResult<SubscriptionSummaryDto>> ListSubscriptionsAsync(
        int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var (shards, byTenant) = await ShardMapAsync(ct);

        var all = new List<SubscriptionSummaryDto>();
        foreach (var cs in shards)
        {
            await using var db = BuildContext(cs, new FixedTenantContext(HostScopeSentinel));
            var subs = await db.Subscriptions
                .IgnoreQueryFilters(["Tenant"])
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var s in subs)
            {
                byTenant.TryGetValue(s.TenantId, out var t);
                var tier = t?.Tier ?? TenantTier.Free;
                all.Add(new SubscriptionSummaryDto(
                    s.TenantId, t?.Name ?? s.TenantId, s.RazorpaySubscriptionId,
                    tier.ToString(), s.Status, NextBillingUtc: null, MonthlyPriceInPaise(tier)));
            }
        }

        var ordered = all.OrderBy(s => s.TenantName).ToList();
        return Page(ordered, pageNumber, pageSize);
    }

    public async Task<SubscriptionMetricsDto> GetSubscriptionMetricsAsync(CancellationToken ct = default)
    {
        var (shards, byTenant) = await ShardMapAsync(ct);

        var all = new List<Subscription>();
        foreach (var cs in shards)
        {
            await using var db = BuildContext(cs, new FixedTenantContext(HostScopeSentinel));
            all.AddRange(await db.Subscriptions.IgnoreQueryFilters(["Tenant"]).AsNoTracking().ToListAsync(ct));
        }

        var active = all.Count(s => s.Status == "active");
        var cancelled = all.Count(s => s.Status == "cancelled");
        var pending = all.Count(s => s.Status == "pending_cancellation");
        var total = active + cancelled;

        var mrr = all
            .Where(s => s.Status == "active")
            .Sum(s => MonthlyPriceInPaise(byTenant.TryGetValue(s.TenantId, out var t) ? t.Tier : TenantTier.Free));

        var churn = total > 0 ? Math.Round(cancelled * 100.0 / total, 2) : 0;

        return new SubscriptionMetricsDto(active, mrr, churn, pending);
    }

    public Task<bool> ExtendTrialAsync(string razorpaySubscriptionId, int days, CancellationToken ct = default) =>
        // No local trial-end field exists; the real action is a Razorpay subscription-management API
        // call (the seam belongs here once that client is added). For now confirm the subscription
        // exists so the host UI can report success/not-found without fabricating persisted state.
        SubscriptionExistsAsync(razorpaySubscriptionId, ct);

    public Task<bool> ApplyDiscountAsync(string razorpaySubscriptionId, int percent, CancellationToken ct = default) =>
        // As with ExtendTrial: a discount is applied via the Razorpay management API. Confirm
        // existence only until that client is wired in.
        SubscriptionExistsAsync(razorpaySubscriptionId, ct);

    public async Task<bool> CancelSubscriptionAsync(
        string razorpaySubscriptionId, CancellationToken ct = default)
    {
        if (await LocateSubscriptionAsync(razorpaySubscriptionId, ct) is not { } located)
            return false;
        var (tenantId, connectionString) = located;

        // Bind the context to the owning tenant so the cancellation is tenant-scoped and the audit
        // writer stamps the correct tenant id + the acting host operator.
        await using var db = BuildContext(connectionString, new FixedTenantContext(tenantId));
        var sub = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.RazorpaySubscriptionId == razorpaySubscriptionId, ct);
        if (sub is null)
            return false;

        sub.Status = "cancelled";
        sub.CancelledAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Module 3: cross-tenant audit browser ------------------------------------------------

    public async Task<PagedResult<AuditLogEntryDto>> SearchAuditAsync(
        AuditQuery query, CancellationToken ct = default)
    {
        var (shards, byTenant) = await ShardMapAsync(ct);

        // A tenant filter narrows the search to that tenant's shard only; otherwise scan every shard.
        IEnumerable<string> targets = shards;
        if (!string.IsNullOrWhiteSpace(query.TenantId)
            && byTenant.TryGetValue(query.TenantId, out var only))
        {
            targets = [only.ShardConnectionString];
        }

        var rows = new List<AuditLog>();
        foreach (var cs in targets.Distinct())
        {
            await using var db = BuildContext(cs, new FixedTenantContext(HostScopeSentinel));
            var q = db.AuditLogs.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.TenantId))
                q = q.Where(a => a.TenantId == query.TenantId);
            if (!string.IsNullOrWhiteSpace(query.UserId))
                q = q.Where(a => a.UserId.Contains(query.UserId));
            if (!string.IsNullOrWhiteSpace(query.Action))
                q = q.Where(a => a.Action.Contains(query.Action));
            if (!string.IsNullOrWhiteSpace(query.TableName))
                q = q.Where(a => a.TableName.Contains(query.TableName));
            if (query.From is { } from)
                q = q.Where(a => a.Timestamp >= from);
            if (query.To is { } to)
                q = q.Where(a => a.Timestamp <= to);

            rows.AddRange(await q.ToListAsync(ct));
        }

        var ordered = rows
            .OrderByDescending(a => a.Timestamp)
            .Select(ToAuditDto)
            .ToList();

        return Page(ordered, query.PageNumber, query.PageSize);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private ApplicationDbContext BuildContext(string connectionString, ITenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new ApplicationDbContext(options, tenant, currentUser);
    }

    private async Task<TenantShard> RequireShardAsync(string tenantId, CancellationToken ct) =>
        await catalog.TenantShards.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw new KeyNotFoundException($"Tenant '{tenantId}' is not registered in the catalog.");

    private async Task<(IReadOnlyList<string> Shards, Dictionary<string, TenantShard> ByTenant)> ShardMapAsync(
        CancellationToken ct)
    {
        var all = await catalog.TenantShards.AsNoTracking().ToListAsync(ct);
        var shards = all.Select(t => t.ShardConnectionString).Distinct().ToList();
        var byTenant = all.ToDictionary(t => t.TenantId);
        return (shards, byTenant);
    }

    private async Task<(string TenantId, string ConnectionString)?> LocateSubscriptionAsync(
        string razorpaySubscriptionId, CancellationToken ct)
    {
        var (shards, _) = await ShardMapAsync(ct);
        foreach (var cs in shards)
        {
            await using var db = BuildContext(cs, new FixedTenantContext(HostScopeSentinel));
            var tenantId = await db.Subscriptions
                .IgnoreQueryFilters(["Tenant"])
                .AsNoTracking()
                .Where(s => s.RazorpaySubscriptionId == razorpaySubscriptionId)
                .Select(s => s.TenantId)
                .FirstOrDefaultAsync(ct);
            if (tenantId is not null)
                return (tenantId, cs);
        }

        return null;
    }

    private async Task<bool> SubscriptionExistsAsync(string razorpaySubscriptionId, CancellationToken ct) =>
        await LocateSubscriptionAsync(razorpaySubscriptionId, ct) is not null;

    private static PagedResult<T> Page<T>(IReadOnlyList<T> all, int pageNumber, int pageSize)
    {
        var items = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<T>(items, all.Count, pageNumber, pageSize);
    }

    private static TenantSummaryDto ToSummary(TenantShard t) =>
        new(t.TenantId, t.Name, t.Tier.ToString(), t.ShardLabel, t.Status.ToString(), t.CreatedAt);

    private static TenantDetailDto ToDetail(TenantShard t) =>
        new(t.TenantId, t.Name, t.Tier.ToString(), t.ShardLabel, t.Status.ToString(),
            t.CreatedAt, t.AdminEmail, t.Region);

    private static AuditLogEntryDto ToAuditDto(AuditLog a) =>
        new(a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            a.TenantId, a.UserId, a.Action, a.TableName, a.Timestamp,
            $"{a.Action} {a.TableName}");

    private static long QuotaBytes(TenantTier tier) => tier switch
    {
        TenantTier.Enterprise => 1_000L * 1024 * 1024 * 1024, // 1 TB
        TenantTier.Pro => 100L * 1024 * 1024 * 1024,          // 100 GB
        _ => 5L * 1024 * 1024 * 1024,                          // 5 GB
    };

    // Dev price-book by tier. Production reads the authoritative amount from the Razorpay plan.
    private static long MonthlyPriceInPaise(TenantTier tier) => tier switch
    {
        TenantTier.Enterprise => 999900,
        TenantTier.Pro => 299900,
        _ => 0,
    };
}
