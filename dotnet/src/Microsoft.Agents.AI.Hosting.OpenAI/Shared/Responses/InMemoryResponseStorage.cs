// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

#if AGENTGATEWAY
using AgentGateway.Responses.Models;

namespace AgentGateway.Responses;
#else
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;
#endif

/// <summary>
/// In-memory implementation of response storage using MemoryCache.
/// This implementation is thread-safe but data is not persisted across application restarts.
/// </summary>
internal sealed class InMemoryResponseStorage : IResponseStorage, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly InMemoryStorageOptions _options;

    private sealed class StoredResponse
    {
        private int _etagVersion;

        public required Response Response { get; set; }
        public string ETag => $"\"{this._etagVersion}\"";

        public string IncrementETag()
        {
            Interlocked.Increment(ref this._etagVersion);
            return this.ETag;
        }
    }

    public InMemoryResponseStorage()
        : this(new InMemoryStorageOptions())
    {
    }

    public InMemoryResponseStorage(InMemoryStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this._options = options;
        this._cache = new MemoryCache(options.ToMemoryCacheOptions());
    }

    public Task<StorageResult<Response>> StoreResponseAsync(
        Response response,
        CancellationToken cancellationToken = default)
    {
        StoredResponse stored = new()
        {
            Response = response
        };

        MemoryCacheEntryOptions entryOptions = this._options.ToMemoryCacheEntryOptions();

        this._cache.Set(response.Id, stored, entryOptions);

        return Task.FromResult(new StorageResult<Response>
        {
            Value = response,
            ETag = stored.ETag
        });
    }

    public Task<StorageResult<Response>?> GetResponseAsync(
        string responseId,
        CancellationToken cancellationToken = default)
    {
        if (!this._cache.TryGetValue(responseId, out StoredResponse? stored) || stored is null)
        {
            return Task.FromResult<StorageResult<Response>?>(null);
        }

        return Task.FromResult<StorageResult<Response>?>(new StorageResult<Response>
        {
            Value = stored.Response,
            ETag = stored.ETag
        });
    }

    public Task<StorageResult<Response>?> UpdateResponseAsync(
        Response response,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        if (!this._cache.TryGetValue(response.Id, out StoredResponse? stored) || stored is null)
        {
            return Task.FromResult<StorageResult<Response>?>(null);
        }

        // Check ETag for optimistic concurrency
        if (stored.ETag != expectedETag)
        {
            return Task.FromResult<StorageResult<Response>?>(null);
        }

        // Update the response and increment the ETag
        stored.Response = response;
        string newETag = stored.IncrementETag();

        return Task.FromResult<StorageResult<Response>?>(new StorageResult<Response>
        {
            Value = response,
            ETag = newETag
        });
    }

    public Task<bool> DeleteResponseAsync(
        string responseId,
        CancellationToken cancellationToken = default)
    {
        this._cache.Remove(responseId);
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        this._cache.Dispose();
    }
}
