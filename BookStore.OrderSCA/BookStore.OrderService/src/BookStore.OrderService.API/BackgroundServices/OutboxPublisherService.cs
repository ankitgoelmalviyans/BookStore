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

                // Only OrderCreated is emitted in this increment; deserialize to the typed event so
                // the producer serializes it consistently (and so a future EventType switch has an
                // obvious seam).
                var payload = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload);
                if (payload is null)
                {
                    _logger.LogWarning(
                        "Outbox record {EventId} has an unparseable payload; skipping", message.EventId);
                    continue;
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
}
