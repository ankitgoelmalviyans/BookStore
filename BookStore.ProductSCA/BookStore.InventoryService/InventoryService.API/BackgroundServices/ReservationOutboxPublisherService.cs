using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using BookStore.InventoryService.Domain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace BookStore.InventoryService.API.BackgroundServices
{
    /// <summary>
    /// Drains the embedded outbox on <see cref="OrderReservation"/> documents: publishes Pending
    /// InventoryReserved / …Failed events to <c>inventory-events</c> and marks them Published, with a
    /// bounded retry → terminal Failed (ADR-17). All dependencies are singletons in InventoryService,
    /// so no per-cycle scope is needed.
    /// </summary>
    public class ReservationOutboxPublisherService : BackgroundService
    {
        private readonly IReservationRepository _reservations;
        private readonly IMessagePublisher _publisher;
        private readonly ILogger<ReservationOutboxPublisherService> _logger;
        private readonly TimeSpan _pollingInterval;
        private readonly int _batchSize;
        private readonly int _maxRetries;

        public ReservationOutboxPublisherService(
            IReservationRepository reservations,
            IMessagePublisher publisher,
            IConfiguration configuration,
            ILogger<ReservationOutboxPublisherService> logger)
        {
            _reservations = reservations;
            _publisher = publisher;
            _logger = logger;

            var pollingSeconds = configuration.GetValue<int?>("Outbox:PollingIntervalSeconds") ?? 10;
            _batchSize = configuration.GetValue<int?>("Outbox:BatchSize") ?? 20;
            _maxRetries = configuration.GetValue<int?>("Outbox:MaxRetries") ?? 10;

            if (pollingSeconds <= 0)
                throw new ArgumentOutOfRangeException("Outbox:PollingIntervalSeconds", pollingSeconds, "must be greater than zero.");
            if (_batchSize <= 0)
                throw new ArgumentOutOfRangeException("Outbox:BatchSize", _batchSize, "must be greater than zero.");
            if (_maxRetries <= 0)
                throw new ArgumentOutOfRangeException("Outbox:MaxRetries", _maxRetries, "must be greater than zero.");

            _pollingInterval = TimeSpan.FromSeconds(pollingSeconds);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReservationOutboxPublisherService started (interval {IntervalSeconds}s, batch {BatchSize})",
                _pollingInterval.TotalSeconds, _batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PublishPendingAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reservation outbox publish cycle failed; will retry next interval");
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
            var pending = await _reservations.GetPendingOutboxAsync(_batchSize, stoppingToken);

            foreach (var reservation in pending)
            {
                if (stoppingToken.IsCancellationRequested) break;
                var outbox = reservation.Outbox;
                if (outbox is null) continue;

                try
                {
                    await PublishOneAsync(reservation, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex,
                        "Reservation {OrderId} outbox event {EventId} failed to publish (attempt {Attempt}); retry up to {MaxRetries}",
                        reservation.OrderId, outbox.EventId, outbox.RetryCount + 1, _maxRetries);

                    outbox.RetryCount++;
                    if (outbox.RetryCount >= _maxRetries)
                    {
                        outbox.Status = OutboxStatus.Failed;
                        _logger.LogError("Reservation {OrderId} outbox event {EventId} exhausted its retry budget (Failed); manual reconciliation required",
                            reservation.OrderId, outbox.EventId);
                    }
                    reservation.LastUpdated = DateTime.UtcNow;
                    await _reservations.UpsertAsync(reservation, stoppingToken);
                }
            }
        }

        private async Task PublishOneAsync(OrderReservation reservation, CancellationToken stoppingToken)
        {
            var outbox = reservation.Outbox!;
            object? payload = outbox.EventType switch
            {
                nameof(InventoryReservedEvent) => JsonSerializer.Deserialize<InventoryReservedEvent>(outbox.Payload),
                nameof(InventoryReservationFailedEvent) => JsonSerializer.Deserialize<InventoryReservationFailedEvent>(outbox.Payload),
                _ => null
            };

            if (payload is null)
            {
                throw new InvalidOperationException(
                    $"Reservation {reservation.OrderId} outbox has unknown type '{outbox.EventType}' or unparseable payload.");
            }

            using (LogContext.PushProperty("CorrelationId", outbox.CorrelationId))
            {
                await _publisher.PublishAsync(payload, outbox.Topic, outbox.CorrelationId, outbox.TraceParent, outbox.EventType);
                outbox.Status = OutboxStatus.Published;
                outbox.PublishedAt = DateTime.UtcNow;
                reservation.LastUpdated = DateTime.UtcNow;
                await _reservations.UpsertAsync(reservation, stoppingToken);

                _logger.LogInformation("Reservation {OrderId} outbox event {EventId} ({EventType}) published to '{Topic}'",
                    reservation.OrderId, outbox.EventId, outbox.EventType, outbox.Topic);
            }
        }
    }
}
