// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace AgentGateway.Utilities;

internal sealed class AsyncManualResetEvent
{
    private readonly object _lock = new();
    private TaskCompletionSource _event = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource completion;
        lock (this._lock)
        {
            completion = this._event;
        }

        await completion.Task.WaitAsync(cancellationToken);
    }

    public void SignalAndReset()
    {
        TaskCompletionSource completion;

        lock (this._lock)
        {
            completion = this._event;
            this._event = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        completion.TrySetResult();
    }

    public void Cancel()
    {
        TaskCompletionSource completion;

        lock (this._lock)
        {
            completion = this._event;
        }

        completion.TrySetCanceled();
    }
}
