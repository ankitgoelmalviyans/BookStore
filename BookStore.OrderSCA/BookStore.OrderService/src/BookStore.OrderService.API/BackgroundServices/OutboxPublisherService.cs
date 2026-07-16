using System.Text.Json;
using BookStore.OrderService.Core.Entities;
using BookStore.OrderService.Core.Events;
using BookStore.OrderService.Core.Messaging;
using Serilog.Context;

namespace BookStore.OrderService.API.BackgroundServices
{
    /// <summary>
    /// Drains the transactional outbox (<c>OrderOutbox</c> table). On a fixed interval it finds records
    /// whose status is still <see cref="OutboxMessage.Pending"/>, publishes the event to Service Bus
    /// (re-using the stored CorrelationId + traceparent so the async trace survives), and marks the
    /// record <see cref="OutboxMessage.Published"/>.
    ///
    /// Delivery is at-least-once: if the process dies between publishing and marking, the record is
    /// re-published next cycle. That is safe because every downstream consumer deduplicates on the
    /// event id via its own Inbox (docs/TRD.md ADR-17).
    /// </summary>
    public class OutboxPublisherService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OutboxPublisherService> _logger;
        private readonly TimeSpan _pollingInterval;
        private readonly int _batchSize;
        private readonly int _maxRetries;

        public OutboxPublisherService(
            IServiceScopeFactory scopeFactory,
            ILogger<OutboxPublisherService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _pollingInterval = TimeSpan.FromSeconds(
                configuration.GetValue<int?>("Outbox:PollingIntervalSeconds") ?? 10);
            _batchSize = configuration.GetValue<int?>("Outbox:BatchSize") ?? 20;
            _maxRetries = configuration.GetValue<int?>("Outbox:MaxRetries") ?? 10;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "OutboxPublisherService started (interval {IntervalSeconds}s, batch {BatchSize})",
                _pollingInterval.TotalSeconds, _batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PublishPendingAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // A whole cycle failing (e.g. SQL/Service Bus transient outage) must not crash the
                    // host — log and try again next interval.
                    _logger.LogError(ex, "Outbox publish cycle failed; will retry next interval");
                }

                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PublishPendingAsync(CancellationToken stoppingToken)
        {
            // BackgroundService is a Singleton; the store and publisher are Scoped, so open a scope.
            using var scope = _scopeFactory.CreateScope();
            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

            var pending = await outboxStore.GetPendingAsync(_batchSize, stoppingToken);

            foreach (var message in pending)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                // Isolate each record: a single malformed payload or a transient publish error must
                // not abort the whole batch (which would also let one poison record at the head block
                // every newer one). On failure we increment the retry count and, past the budget,
                // move the record to the terminal Failed state so it stops being re-fetched.
                try
                {
                    await PublishOneAsync(message, outboxStore, publisher, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex,
                        "Outbox record {EventId} failed to publish (attempt {Attempt}); will retry up to {MaxRetries}",
                        message.EventId, message.RetryCount + 1, _maxRetries);
                    await outboxStore.RecordFailureAsync(message, _maxRetries, stoppingToken);

                    if (message.Status == OutboxMessage.Failed)
                    {
                        _logger.LogError(
                            "Outbox record {EventId} exhausted its retry budget and was moved to Failed; manual reconciliation required",
                            message.EventId);
                    }
                }
            }
        }

        private async Task PublishOneAsync(
            OutboxMessage message,
            IOutboxStore outboxStore,
            IMessagePublisher publisher,
            CancellationToken stoppingToken)
        {
            // Only OrderCreated is emitted in this increment; deserialize to the typed event so the
            // producer serializes it consistently (and so a future EventType switch has an obvious
            // seam). A null/unparseable payload is treated as a failure so it counts toward the retry
            // budget and eventually dead-letters, rather than silently staying Pending forever.
            var payload = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload);
            if (payload is null)
            {
                _logger.LogWarning(
                    "Outbox record {EventId} has an unparseable payload; recording as a failed attempt", message.EventId);
                await outboxStore.RecordFailureAsync(message, _maxRetries, stoppingToken);
                return;
            }

            using (LogContext.PushProperty("CorrelationId", message.CorrelationId))
            {
                await publisher.PublishAsync(
                    payload, message.Topic, message.CorrelationId, message.TraceParent);
                await outboxStore.MarkPublishedAsync(message, stoppingToken);

                _logger.LogInformation(
                    "Outbox event {EventId} ({EventType}) published to topic '{Topic}'",
                    message.EventId, message.EventType, message.Topic);
            }
        }
    }
}
