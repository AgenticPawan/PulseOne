using PulseOne.Application.Features.Billing;
using StackExchange.Redis;

namespace PulseOne.Infrastructure.Billing;

/// <summary>
/// Redis-backed idempotency gate for Razorpay webhooks (blueprint §6.3, Appendix A defect #12).
/// Uses an atomic <c>SET key value NX EX ttl</c> (StackExchange.Redis <see cref="When.NotExists"/>) so
/// the "have I seen this event id?" test and the "claim it" write are a SINGLE round-trip — two
/// concurrent deliveries of the same <c>X-Razorpay-Event-Id</c> can never both succeed.
/// </summary>
/// <remarks>
/// The 7-day TTL (supplied by the caller per the security rule) comfortably outlasts Razorpay's retry
/// window while letting the key expire so the dedup namespace does not grow unbounded. The
/// <see cref="IConnectionMultiplexer"/> is the singleton already registered in the producer's
/// composition root (lazy, resilient connect).
/// </remarks>
public sealed class RedisWebhookDeduplicationStore(IConnectionMultiplexer redis) : IWebhookDeduplicationStore
{
    /// <inheritdoc />
    public async Task<bool> TryMarkProcessedAsync(string eventId, TimeSpan ttl, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var key = $"webhook:processed:{eventId}";

        // SETNX with expiry — returns true ONLY if the key did not already exist (first delivery).
        return await db.StringSetAsync(key, "1", ttl, When.NotExists);
    }
}
