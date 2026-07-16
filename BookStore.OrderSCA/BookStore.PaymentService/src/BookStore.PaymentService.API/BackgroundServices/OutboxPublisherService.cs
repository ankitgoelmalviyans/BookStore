using System.Text.Json;
using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Events;
using BookStore.PaymentService.Core.Messaging;
using Serilog.Context;

namespace BookStore.PaymentService.API.BackgroundServices
{
    /// <summary>
    /// Drains the <c>PaymentOutbox</c> table: publishes Pending records (PaymentProcessed /
    /// PaymentFailed) to the <c>payment-events</c> topic and marks them Published. Per-record
    /// isolation with a bounded retry → terminal Failed, identical to OrderService's drain
    /// (docs/TRD.md ADR-17). At-least-once delivery is safe because every consumer dedupes.
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

            var pollingSeconds = configuration.GetValue<int?>("Outbox:PollingIntervalSeconds") ?? 10;
            _batchSize = configuration.GetValue<int?>("Outbox:BatchSize") ?? 20;
            _maxRetries = configuration.GetValue<int?>("Outbox:MaxRetries") ?? 10;

            // Reject non-positive settings at startup (the `?? default` only guards a MISSING key).
            if (pollingSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "Outbox:PollingIntervalSeconds", pollingSeconds, "Outbox:PollingIntervalSeconds must be greater than zero.");
            }
            if (_batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "Outbox:BatchSize", _batchSize, "Outbox:BatchSize must be greater than zero.");
            }
            if (_maxRetries <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "Outbox:MaxRetries", _maxRetries, "Outbox:MaxRetries must be greater than zero.");
            }

            _pollingInterval = TimeSpan.FromSeconds(pollingSeconds);
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
            // Deserialize to the concrete outbound event type so the producer serializes it
            // consistently. A null/unparseable payload counts as a failed attempt (→ eventually
            // dead-lettered) rather than silently staying Pending forever.
            object? payload = message.EventType switch
            {
                nameof(PaymentProcessedEvent) => JsonSerializer.Deserialize<PaymentProcessedEvent>(message.Payload),
                nameof(PaymentFailedEvent) => JsonSerializer.Deserialize<PaymentFailedEvent>(message.Payload),
                _ => null
            };

            if (payload is null)
            {
                _logger.LogWarning(
                    "Outbox record {EventId} has an unknown type '{EventType}' or unparseable payload; recording as a failed attempt",
                    message.EventId, message.EventType);
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
