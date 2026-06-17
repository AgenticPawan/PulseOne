namespace PulseOne.SharedKernel.Caching;

/// <summary>
/// Distributed cache abstraction. Backed by Redis in production
/// (<see cref="RedisCacheService"/>). Values are serialized as JSON.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
