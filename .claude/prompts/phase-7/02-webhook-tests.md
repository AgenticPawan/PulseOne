# Prompt: Webhook Test Suite — Signature, Idempotency, Constant-Time

## Context
Blueprint §7.1 specifies four required tests. These tests exist to catch the v1 defects: hardcoded secret, non-constant-time comparison, and duplicate event processing.

## Task
Implement the complete webhook test suite in `PulseOne.Application.Tests/Billing/`.

### Test Setup
```csharp
public class RazorpayWebhookTests
{
    private readonly IRazorpayWebhookVerifier _verifier;
    private readonly IWebhookDeduplicationStore _dedupe;
    private readonly IBackgroundJobClient _jobs;
    private readonly ProcessRazorpayWebhookHandler _handler;

    private const string TestSecret = "test_webhook_secret_not_a_real_value";
    private const string TestBody   = """{"event":"payment.captured","payload":{"payment":{"entity":{"id":"pay_test123"}}}}""";

    public RazorpayWebhookTests()
    {
        var opts = new OptionsMonitorMock<RazorpayOptions>(new() { WebhookSecret = TestSecret });
        _verifier = new RazorpayWebhookVerifier(opts);
        _dedupe   = new InMemoryDeduplicationStore();   // test double
        _jobs     = Substitute.For<IBackgroundJobClient>();
        _handler  = new ProcessRazorpayWebhookHandler(_verifier, _dedupe, _jobs, NullLogger<...>.Instance);
    }

    private static string ComputeSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLower();
    }
}
```

### Required Tests (all four from §7.1)
```csharp
[Fact] public async Task Rejects_when_signature_invalid()
// Act: send wrong signature → outcome must be InvalidSignature, job NOT enqueued

[Fact] public async Task Verifies_when_signature_valid_and_enqueues_once()
// Act: send correct HMAC-SHA256 hex signature → outcome Verified, job enqueued exactly once

[Fact] public async Task Suppresses_duplicate_event_id()
// Arrange: mark event "evt_123" as processed in dedupe store
// Act: send same event again with valid signature → outcome Duplicate, job NOT enqueued

[Fact] public void Verifier_is_constant_time()
// This proves the FixedTimeEquals path is exercised for equal-length comparisons
// Verify that two equal-length inputs (one correct, one incorrect) both reach FixedTimeEquals
// Use a test-double verifier that captures whether FixedTimeEquals was called
```

### Additional Tests (beyond blueprint minimum)
```csharp
[Fact] public async Task Malformed_hex_signature_returns_InvalidSignature()
// "not-hex-at-all" signature → catches FormatException → returns false

[Fact] public async Task Empty_event_id_is_treated_as_unique()
// Empty string eventId should not collide with other events (Redis key must be unique)

[Fact] public async Task Webhook_endpoint_returns_200_for_duplicate()
// Integration test: duplicate event → 200 OK (so Razorpay stops retrying)

[Fact] public async Task Webhook_endpoint_returns_400_for_bad_signature()
// Integration test: invalid signature → 400 Bad Request
```

### Host Boundary Test (§7.3)
```csharp
[Fact]
public async Task Tenant_principal_calling_host_endpoint_gets_403()
{
    var client = _factory.WithTenantPrincipal(role: "tenant-admin").CreateClient();
    var res = await client.GetAsync("/api/v1/host/tenants");
    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

## Output Location
`src/backend/PulseOne.Application.Tests/Billing/`
`src/backend/PulseOne.WebApi.Tests/Authorization/`

## Constraints
- Use `NSubstitute` for mocking (not Moq — NSubstitute is preferred for this project)
- `InMemoryDeduplicationStore` must faithfully simulate SETNX: first call true, subsequent calls false for same key
- The "constant-time" test proves the code PATH goes through `FixedTimeEquals` — you cannot meaningfully test timing in a unit test
- Integration tests use `WebApplicationFactory<Program>`
