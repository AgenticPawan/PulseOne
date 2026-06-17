# Prompt: Razorpay Webhook — Secure, Constant-Time, Idempotent

## Context
This is a security-critical component. Blueprint §6.3 documents three v1 defects:
1. Live secret hardcoded in source
2. Non-constant-time signature comparison (timing leak)
3. No idempotency (Razorpay retries would double-apply payments)

All three are fixed in v2. Implement exactly as specified.

## Task

### Webhook Verifier (blueprint §6.3 — copy verbatim, then extend)
- `RazorpayOptions` bound from Key Vault section `"Razorpay"` — `WebhookSecret` field
- `RazorpayWebhookVerifier` using `HMACSHA256` + `CryptographicOperations.FixedTimeEquals`
- `Convert.FromHexString(signatureHex)` — catches `FormatException`, returns `false`
- HMAC instance disposed after use

### Deduplication Store
```csharp
public interface IWebhookDeduplicationStore
{
    Task<bool> TryMarkProcessedAsync(string eventId, TimeSpan ttl, CancellationToken ct);
}

public sealed class RedisWebhookDeduplicationStore(IConnectionMultiplexer redis) : IWebhookDeduplicationStore
{
    public async Task<bool> TryMarkProcessedAsync(string eventId, TimeSpan ttl, CancellationToken ct)
    {
        var db  = redis.GetDatabase();
        var key = $"webhook:processed:{eventId}";
        return await db.StringSetAsync(key, "1", ttl, When.NotExists);   // SETNX — returns true only if key was new
    }
}
```

### MediatR Handler (blueprint §6.3)
Implement `ProcessRazorpayWebhookHandler` exactly as in blueprint §6.3:
- Verify signature → `InvalidSignature` if bad
- Deduplicate → `Duplicate` if already seen
- Enqueue `IRazorpaySubscriptionProcessor` via `IBackgroundJobClient`
- Return `WebhookOutcome`

### Subscription Processor Worker (`PulseOne.BackgroundWorker`)
```csharp
public sealed class RazorpaySubscriptionProcessor(ApplicationDbContext db, ILogger<...> log)
    : IRazorpaySubscriptionProcessor
{
    [Queue("critical")]
    [AutomaticRetry(Attempts = 5)]
    public async Task ApplyAsync(string rawBody, string eventId, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<RazorpayWebhookPayload>(rawBody);
        switch (payload?.Event)
        {
            case "subscription.activated": await ActivateSubscription(payload, ct); break;
            case "payment.captured":       await RecordPayment(payload, ct); break;
            case "subscription.cancelled": await CancelSubscription(payload, ct); break;
        }
    }
}
```

### Endpoint (blueprint §6.3)
- Reads raw body with `StreamReader` (required for HMAC — do NOT use model binding which re-serializes)
- Applies `RequireRateLimiting("webhook")`
- Always returns `200 OK` for verified/duplicate; `400 BadRequest` for bad signature only

### Public Config Endpoint (blueprint §6.5)
```csharp
app.MapGet("/api/v1/config/public", (IOptions<RazorpayOptions> opts) =>
    Results.Ok(new { razorpayKeyId = opts.Value.KeyId }))
    .AllowAnonymous();
// KeyId is the PUBLISHABLE key — safe to expose. WebhookSecret is NEVER exposed.
```

## Output Locations
- `src/backend/PulseOne.Application/Features/Billing/RazorpayWebhookVerifier.cs`
- `src/backend/PulseOne.Application/Features/Billing/Commands/ProcessRazorpayWebhookHandler.cs`
- `src/backend/PulseOne.Infrastructure/Billing/RedisWebhookDeduplicationStore.cs`
- `src/backend/PulseOne.BackgroundWorker/Jobs/RazorpaySubscriptionProcessor.cs`
- `src/backend/PulseOne.WebApi/Endpoints/BillingEndpoints.cs`

## Constraints
- `WebhookSecret` MUST come from `IOptionsMonitor<RazorpayOptions>` — not `IConfiguration` directly
- `Convert.FromHexString` is case-insensitive — do NOT call `.ToLower()` on the header value
- `HMACSHA256` MUST be disposed (use `using var`)
- The raw body string must be read BEFORE any other middleware reads the stream
- Rate limit: 100 requests/minute on the webhook endpoint
