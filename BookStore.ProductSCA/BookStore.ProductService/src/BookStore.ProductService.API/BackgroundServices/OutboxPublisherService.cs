using BookStore.ProductService.Core.Entities;
using BookStore.ProductService.Core.Events;
using BookStore.ProductService.Core.Messaging;
using Serilog.Context;

namespace BookStore.ProductService.API.BackgroundServices
{
    /// <summary>
    /// Drains the transactional outbox. On a fixed interval it finds Product documents whose embedded
    /// outbox record is still <see cref="OutboxMessage.Pending"/>, publishes the event to Service Bus
    /// (re-using the stored CorrelationId so the async trace survives), and marks the record
    /// <see cref="OutboxMessage.Published"/>.
    ///
    /// Delivery is at-least-once: if the process dies between publishing and marking, the record is
    /// re-published next cycle. That is safe because the InventoryService consumer is idempotent — it
    /// sets an absolute quantity from the event, so processing the same event twice is a no-op.
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
                    // A whole cycle failing (e.g. Cosmos/Service Bus transient outage) must not crash
                    // the host — log and try again next interval.
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

            foreach (var product in pending)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var outbox = product.Outbox;
                if (outbox is null)
                {
                    continue;
                }

                // Only one of these is populated, selected by EventType (see OutboxMessage) — Create
                // still uses the original Payload field, Update/Delete use their own typed fields so
                // each round-trips through Cosmos as its own concrete type.
                object? eventPayload = outbox.EventType switch
                {
                    nameof(ProductCreatedEvent) => outbox.Payload,
                    nameof(ProductUpdatedEvent) => outbox.UpdatedPayload,
                    nameof(ProductDeletedEvent) => outbox.DeletedPayload,
                    _ => null
                };

                if (eventPayload is null)
                {
                    continue;
                }

                using (LogContext.PushProperty("CorrelationId", outbox.CorrelationId))
                {
                    await publisher.PublishAsync(
                        eventPayload, outbox.Topic, outbox.CorrelationId, outbox.TraceParent);
                    await outboxStore.MarkPublishedAsync(product, stoppingToken);

                    _logger.LogInformation(
                        "Outbox event {EventId} ({EventType}) published to topic '{Topic}'",
                        outbox.EventId, outbox.EventType, outbox.Topic);
                }
            }
        }
    }
}
