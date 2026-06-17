# Prompt: Tenant Isolation Tests — Proves the Filter is Real

## Context
Blueprint §7.2 states: "the v1 stub would fail this test." The tenant query filter was a `return null` stub in v1 — this test suite exists specifically to catch that class of regression. These tests MUST run on every CI build.

## Task
Implement the full tenant isolation test suite using `xUnit` + `FluentAssertions` + EF Core in-memory (or SQLite for named filter support).

### Test Infrastructure
```csharp
public class TenantIsolationTestBase : IAsyncLifetime
{
    protected ApplicationDbContext NewContextFor(string tenantId)
    {
        var tenantCtx = new TenantContext();
        tenantCtx.Resolve(tenantId);
        var currentUser = Mock.Of<ICurrentUser>(u => u.UserId == "test-user" && u.TenantId == tenantId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source=:memory:;Mode=Memory;Cache=Shared")
            .Options;

        var ctx = new ApplicationDbContext(options, tenantCtx, currentUser);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    protected async Task Seed(string tenantId, int rowCount)
    {
        // Use an unrestricted context (bypass filters) to seed test data
    }
}
```

### Required Tests (from blueprint §7.2)
```csharp
[Fact] public async Task Query_as_tenant_A_never_returns_tenant_B_rows()
[Fact] public async Task Soft_deleted_rows_are_invisible_to_queries()
[Fact] public async Task SaveChanges_stamps_TenantId_automatically()
[Fact] public async Task SaveChanges_converts_hard_delete_to_soft_delete()
[Fact] public async Task AuditLog_captures_OldValues_NewValues_KeyValues_on_modify()
[Fact] public async Task AuditLog_is_NOT_filtered_by_tenant_filter()      // audit rows must be readable across contexts
[Fact] public async Task TenantContext_throws_when_unresolved()
[Fact] public async Task Middleware_rejects_mismatch_between_subdomain_and_claim()
```

### Cross-Tenant Boundary Test
```csharp
[Fact]
public async Task Query_as_tenant_A_never_returns_tenant_B_rows()
{
    await Seed(tenant: "A", rows: 3);
    await Seed(tenant: "B", rows: 5);

    using var asA = NewContextFor("A");
    var visible = await asA.Set<Report>().ToListAsync();

    visible.Should().HaveCount(3);
    visible.Should().OnlyContain(r => r.TenantId == "A");
}
```

### Middleware Unit Tests
```csharp
[Fact] public async Task Authenticated_user_with_mismatched_claim_gets_403()
[Fact] public async Task Unknown_tenant_id_gets_400()
[Fact] public async Task Valid_tenant_resolves_and_calls_next()
[Fact] public async Task Unauthenticated_request_resolves_from_subdomain_hint()
```

## Output Location
`src/backend/PulseOne.Infrastructure.Tests/Isolation/`

## Constraints
- Tests must use a REAL EF Core provider (SQLite or SQL Server LocalDB) — not `UseInMemoryDatabase()` which does not enforce query filters the same way
- Every test must be independent — no shared state between tests
- Tests must run in CI without Azure dependencies
- `FluentAssertions` for all assertions — no raw `Assert.*`
