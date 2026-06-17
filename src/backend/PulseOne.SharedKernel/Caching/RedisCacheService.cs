using System.Text.Json;
using StackExchange.Redis;

namespace PulseOne.SharedKernel.Caching;

/// <summary>
/// Redis-backed <see cref="ICacheService"/> using <see cref="IConnectionMultiplexer"/>.
/// JSON serialization; null values are not cached.
/// </summary>
public sealed class RedisCacheService(IConnectionMultiplexer redis) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IDatabase Db => redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await Db.StringGetAsync(key);
        if (value.IsNullOrEmpty)
            return default;

        return JsonSerializer.Deserialize<T>((string)value!, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (value is null)
            return;

        var payload = JsonSerializer.Serialize(value, JsonOptions);
        await Db.StringSetAsync(key, payload, ttl);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        Db.KeyDeleteAsync(key);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Db.KeyExistsAsync(key);
}
