// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AgentContracts.Monitoring;
using AgentContracts.Workflows;
using Microsoft.Extensions.Logging;
using Orleans;

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
        _logger = logger;
    }

    /// <inheritdoc/>
    public void PublishEvent(MonitoringEvent evt)
    {
        _logger.LogDebug("Broadcasting monitoring event: {EventType}", evt.EventType);

        foreach (var (subscriberId, channel) in _subscribers)
        {
            if (!channel.Writer.TryWrite(evt))
            {
                _logger.LogWarning("Failed to write event to subscriber {SubscriberId} - channel may be full", subscriberId);
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
        PublishEvent(evt);
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
        PublishEvent(evt);
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

        _subscribers[subscriberId] = channel;
        _logger.LogDebug("Subscriber {SubscriberId} connected", subscriberId);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return evt;
            }
        }
        finally
        {
            _subscribers.TryRemove(subscriberId, out _);
            _logger.LogDebug("Subscriber {SubscriberId} disconnected", subscriberId);
        }
    }

    /// <summary>
    /// Gets the current number of active subscribers.
    /// </summary>
    public int SubscriberCount => _subscribers.Count;
}
