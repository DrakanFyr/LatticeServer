using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LatticeServer.Services;

public class StoredObject
{
    public required string Path { get; set; }
    public required byte[] Data { get; set; }
    public required string Checksum { get; set; }
    public required DateTime LastUpdatedAt { get; set; }
    public required DateTime ExpiryTime { get; set; }
}

public class ObjectStore
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(90);

    private readonly ConcurrentDictionary<string, StoredObject> _objects = new();

    public StoredObject Upload(string path, byte[] data, long? ttlNanoseconds)
    {
        var checksum = Convert.ToHexStringLower(SHA256.HashData(data));
        var now = DateTime.UtcNow;
        var ttl = ttlNanoseconds.HasValue
            ? TimeSpan.FromTicks(ttlNanoseconds.Value / 100)
            : DefaultTtl;

        var obj = new StoredObject
        {
            Path = path,
            Data = data,
            Checksum = checksum,
            LastUpdatedAt = now,
            ExpiryTime = now.Add(ttl),
        };

        _objects[path] = obj;
        return obj;
    }

    public StoredObject? Get(string path)
    {
        if (_objects.TryGetValue(path, out var obj))
        {
            if (obj.ExpiryTime <= DateTime.UtcNow)
            {
                _objects.TryRemove(path, out _);
                return null;
            }
            return obj;
        }
        return null;
    }

    public bool Delete(string path)
    {
        return _objects.TryRemove(path, out _);
    }

    public IReadOnlyList<StoredObject> List(string? prefix, DateTime? sinceTimestamp, int maxPageSize)
    {
        IEnumerable<StoredObject> query = _objects.Values;
        var now = DateTime.UtcNow;

        // Filter expired
        query = query.Where(o => o.ExpiryTime > now);

        if (!string.IsNullOrEmpty(prefix))
        {
            query = query.Where(o => o.Path.StartsWith(prefix, StringComparison.Ordinal));
        }

        if (sinceTimestamp.HasValue)
        {
            query = query.Where(o => o.LastUpdatedAt >= sinceTimestamp.Value);
        }

        return query
            .OrderBy(o => o.Path)
            .Take(maxPageSize > 0 ? maxPageSize : 1000)
            .ToList();
    }
}
