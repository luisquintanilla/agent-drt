// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using AgentContracts.Monitoring;
using Microsoft.Extensions.Logging;

namespace AgentGateway.Monitoring;

/// <summary>
/// Broadcasts monitoring events to subscribers using channels.
/// </summary>
internal sealed class MonitoringEventBroadcaster : IMonitoringEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<MonitoringEvent>> _subscribers = new();
    private readonly ILogger<MonitoringEventBroadcaster> _logger;

    public MonitoringEventBroadcaster(ILogger<MonitoringEventBroadcaster> logger)
    {
        this._logger = logger;
    }

    /// <inheritdoc/>
    public void PublishEvent(MonitoringEvent evt)
    {
        this._logger.LogDebug("Broadcasting monitoring event: {EventType}", evt.EventType);

        foreach (var (subscriberId, channel) in this._subscribers)
        {
            if (!channel.Writer.TryWrite(evt))
            {
                this._logger.LogWarning("Failed to write event to subscriber {SubscriberId} - channel may be full", subscriberId);
            }
        }
    }

    /// <inheritdoc/>
    public void PublishWorkflowEvent(string eventType, WorkflowEventPayload payload)
    {
        var evt = new MonitoringEvent
        {
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload,
            CorrelationId = payload.RunId
        };
        this.PublishEvent(evt);
    }

    /// <inheritdoc/>
    public void PublishWorkerEvent(string eventType, WorkerEventPayload payload)
    {
        var evt = new MonitoringEvent
        {
            EventType = eventType,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload,
            CorrelationId = payload.WorkerId
        };
        this.PublishEvent(evt);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<MonitoringEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriberId = Guid.NewGuid();
        var channel = Channel.CreateBounded<MonitoringEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        this._subscribers[subscriberId] = channel;
        this._logger.LogDebug("Subscriber {SubscriberId} connected", subscriberId);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            this._subscribers.TryRemove(subscriberId, out _);
            this._logger.LogDebug("Subscriber {SubscriberId} disconnected", subscriberId);
        }
    }

    /// <summary>
    /// Gets the current number of active subscribers.
    /// </summary>
    public int SubscriberCount => this._subscribers.Count;
}
