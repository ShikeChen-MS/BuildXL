// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Threading;
using Azure.Messaging.EventHubs;
using BuildXL.Cache.ContentStore.Distributed.NuCache.InMemory;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// An event hub client which interacts with a test in-process event hub service
    /// </summary>
    public sealed class MemoryEventHubClient : StartupShutdownSlimBase, IEventHubClient
    {
        private readonly EventHub _hub;
        private readonly ReadWriteLock _lock = ReadWriteLock.Create();
        private OperationContext _context;
        private Action<EventData> _handler;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(MemoryEventHubClient));

        /// <nodoc />
        public MemoryEventHubClient(MemoryContentLocationEventStoreConfiguration configuration)
        {
            _hub = configuration.Hub;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            Tracer.Info(context, "Initializing in-memory content location event store.");

            _context = context;
            return base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            SuspendProcessing(context).ThrowIfFailure();

            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        public BoolResult StartProcessing(OperationContext context, EventSequencePoint sequencePoint, IPartitionReceiveHandler processor)
        {
            using (_lock.AcquireWriteLock())
            {
                _handler = ev => Dispatch(ev, processor);
                var events = _hub.SubscribeAndGetEventsStartingAtSequencePoint(sequencePoint, _handler);

                foreach (var eventData in events)
                {
                    _handler(eventData);
                }
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public BoolResult SuspendProcessing(OperationContext context)
        {
            using (_lock.AcquireWriteLock())
            {
                _hub.Unsubscribe(_handler);
                _handler = null;
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public Task SendAsync(OperationContext context, EventData eventData)
        {
            _hub.Send(eventData);
            return BoolResult.SuccessTask;
        }

        private void Dispatch(EventData eventData, IPartitionReceiveHandler processor)
        {
            processor.ProcessEventsAsync(new[] { eventData }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// In-memory event hub for communicating between different event store instances in memory.
        /// </summary>
        public sealed class EventHub
        {
            private readonly List<EventData> _eventStream = new List<EventData>();

            internal IReadOnlyList<EventData> EventStream => _eventStream;

            private readonly object _syncLock = new object();

            private event Action<EventData> OnEvent;

            /// <nodoc />
            public void Send(EventData eventData)
            {
                Action<EventData> handler;

                lock (_syncLock)
                {
                    handler = OnEvent;

                    // We need to modify some readonly values so we create a new instance
                    eventData = new EventDataWrapper(
                        eventBody: eventData.EventBody,
                        properties: eventData.Properties,
                        systemProperties: eventData.SystemProperties,
                        sequenceNumber: (long)_eventStream.Count,
                        offset: eventData.Offset,
                        enqueuedTime: DateTimeOffset.Now,
                        partitionKey: eventData.PartitionKey
                        );

                    _eventStream.Add(eventData);
                }

                handler?.Invoke(eventData);
            }

            internal void Unsubscribe(Action<EventData> handler)
            {
                lock (_syncLock)
                {
                    OnEvent -= handler;
                }
            }

            internal IReadOnlyList<EventData> SubscribeAndGetEventsStartingAtSequencePoint(EventSequencePoint sequencePoint, Action<EventData> handler)
            {
                lock (_syncLock)
                {
                    OnEvent += handler;
                    return _eventStream.SkipWhile(eventData => IsBefore(eventData, sequencePoint)).ToArray();
                }
            }

            private bool IsBefore(EventData eventData, EventSequencePoint sequencePoint)
            {
                if (sequencePoint.SequenceNumber != null)
                {
                    return eventData.SequenceNumber < sequencePoint.SequenceNumber.Value;
                }
                else
                {
                    return eventData.EnqueuedTime.UtcDateTime < sequencePoint.EventStartCursorTimeUtc.Value;
                }
            }
        }
    }
}
