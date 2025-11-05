// Copyright (c) Microsoft. All rights reserved.

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>
/// Implementation of <see cref="IToolCallMemoStorage"/> that scopes memo operations to a specific call ID.
/// </summary>
internal sealed class ToolCallMemoStorage : IToolCallMemoStorage
{
    private readonly IMemoStorage _memoStorage;
    private readonly string _callId;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCallMemoStorage"/> class.
    /// </summary>
    /// <param name="memoStorage">The underlying memo storage implementation.</param>
    /// <param name="callId">The call ID to scope operations to.</param>
    public ToolCallMemoStorage(IMemoStorage memoStorage, string callId)
    {
        ArgumentNullException.ThrowIfNull(memoStorage);
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);

        this._memoStorage = memoStorage;
        this._callId = callId;
    }

    /// <inheritdoc/>
    public Task<Memo> GetMemoAsync(CancellationToken cancellationToken = default)
    {
        return this._memoStorage.GetMemoAsync(this._callId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Memo> SetMemoAsync(Memo memo, CancellationToken cancellationToken = default)
    {
        return this._memoStorage.SetMemoAsync(this._callId, memo, cancellationToken);
    }
}
