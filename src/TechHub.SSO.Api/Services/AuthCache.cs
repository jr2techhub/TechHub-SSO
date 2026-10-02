using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace TechHub.SSO.Api.Services;

/// <summary>
/// Caché de dos capas (rendimiento):
///   L1: <see cref="IMemoryCache"/> local (sub-µs, por instancia).
///   L2: distributed cache opcional (Redis) compartida entre instancias.
/// Si no hay IDistributedCache registrada (Redis no configurado), funciona solo con L1.
/// Las claves se invalidan explícitamente cuando cambia el estado del tenant/usuario.
/// </summary>
public interface IAuthCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;
    Task RemoveAsync(string key, CancellationToken ct = default);
}

public sealed class AuthCache(
    IMemoryCache memory,
    Microsoft.Extensions.Caching.Distributed.IDistributedCache? distributed = null) : IAuthCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        if (memory.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        if (distributed is not null)
        {
            var bytes = await distributed.GetAsync(key, ct);
            if (bytes is not null)
            {
                var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
                if (value is not null)
                {
                    // Reabastecer L1 desde L2.
                    memory.Set(key, value, TimeSpan.FromSeconds(30));
                    return value;
                }
            }
        }

        return null;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
    {
        // L1 con TTL recortado: las entradas negativas/cached frescas viven poco para
        // limitar la ventana de desincronización entre instancias.
        memory.Set(key, value, ttl);

        if (distributed is not null)
        {
            await distributed.SetAsync(key,
                JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                ct);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        memory.Remove(key);
        if (distributed is not null)
            await distributed.RemoveAsync(key, ct);
    }
}
