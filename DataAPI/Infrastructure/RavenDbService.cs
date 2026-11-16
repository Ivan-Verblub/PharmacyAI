using DataAPI.Common;
using DataAPI.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using System.Text.Json;

namespace DataAPI.Infrastructure;

public interface IRavenDbService
{
    IAsyncDocumentSession OpenSession();
    IMemoryCache Cache { get; }
    void InvalidateCache(params string[] prefixes);
}

public class RavenDbService : IRavenDbService, IDisposable
{
    private readonly DocumentStore _store;
    private readonly TimeSpan _aggressiveCacheDuration;

    public IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

    public RavenDbService(IOptions<RavenDbOptions> options)
    {
        var settings = options.Value;

        _aggressiveCacheDuration = TimeSpan.FromSeconds(settings.AggressiveCacheDurationSeconds);

        _store = new DocumentStore
        {
            Urls = settings.Urls,
            Database = settings.Database
        };

        _store.Conventions.FindCollectionName = type => type.Name + "s";

        _store.Initialize();
    }

    public IAsyncDocumentSession OpenSession()
    {
        var session = _store.OpenAsyncSession();

        session.Advanced.DocumentStore.AggressivelyCacheFor(_aggressiveCacheDuration);

        return session;
    }

    public void InvalidateCache(params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (Cache.TryGetValue(CacheKeys.IndexKey(prefix), out List<string>? keys) && keys is not null)
            {
                foreach (var key in keys)
                {
                    Cache.Remove(key);
                }

                Cache.Remove(CacheKeys.IndexKey(prefix));
            }
        }
    }

    public void Dispose() => _store.Dispose();
}