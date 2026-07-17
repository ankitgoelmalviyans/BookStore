using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BookStore.InventoryService.Application.Interfaces;
using BookStore.InventoryService.Domain;
using BookStore.InventoryService.Domain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookStore.InventoryService.Application.Services
{
    public interface IReservationService
    {
        Task ReserveAsync(OrderCreatedIntegrationEvent order, string? correlationId, string? traceParent, CancellationToken cancellationToken = default);
        Task ReleaseForCancelAsync(OrderCancelledIntegrationEvent cancelled, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Core reservation logic, kept transport-free so it's unit-testable without Service Bus. On
    /// OrderCreated it reserves each line's stock and writes one <see cref="OrderReservation"/>
    /// document recording the outcome plus the embedded outbox event; on OrderCancelled it flags the
    /// order's reserved lines for release. Physical release of flagged lines and publishing of the
    /// outbox event are done by the two background workers. See docs/HLD.md §6 and docs/ROADMAP.md.
    /// </summary>
    public class ReservationService : IReservationService
    {
        private readonly IInventoryRepository _inventory;
        private readonly IReservationRepository _reservations;
        private readonly IInboxStore _inbox;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReservationService> _logger;

        public ReservationService(
            IInventoryRepository inventory,
            IReservationRepository reservations,
            IInboxStore inbox,
            IConfiguration configuration,
            ILogger<ReservationService> logger)
        {
            _inventory = inventory;
            _reservations = reservations;
            _inbox = inbox;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task ReserveAsync(OrderCreatedIntegrationEvent order, string? correlationId, string? traceParent, CancellationToken cancellationToken = default)
        {
            if (order is null) throw new ArgumentNullException(nameof(order));

            // Inbox dedup + aggregate existence check: a redelivery must not reserve twice.
            if (order.EventId != Guid.Empty && await _inbox.HasBeenProcessedAsync(order.EventId, cancellationToken))
            {
                _logger.LogInformation("Duplicate OrderCreated {EventId} — already processed, skipping", order.EventId);
                return;
            }
            if (await _reservations.GetByOrderIdAsync(order.OrderId, cancellationToken) is not null)
            {
                _logger.LogInformation("Reservation for order {OrderId} already exists — skipping", order.OrderId);
                await MarkProcessedAsync(order.EventId, cancellationToken);
                return;
            }

            var topic = _configuration["AzureServiceBus:OutboundTopic"] ?? "inventory-events";
            var currency = _configuration["Payments:Currency"] ?? "usd";

            // Reserve line by line. Each product lives in its own Cosmos partition, so this is N single
            // writes, not one batch — hence the durable OrderReservation record below.
            var reserved = new List<ReservationLine>();
            var failureReason = string.Empty;
            foreach (var item in order.Items)
            {
                if (_inventory.TryReserve(item.ProductId, item.Quantity))
                {
                    reserved.Add(new ReservationLine
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        Status = ReservationLineStatus.Reserved
                    });
                }
                else
                {
                    failureReason = $"Insufficient stock for product {item.ProductId}";
                    break;
                }
            }

            var now = DateTime.UtcNow;
            var outboundEventId = Guid.NewGuid();
            var allReserved = string.IsNullOrEmpty(failureReason);

            OrderReservation reservation;
            if (allReserved)
            {
                var payload = new InventoryReservedEvent
                {
                    EventId = outboundEventId,
                    OrderId = order.OrderId,
                    CustomerId = order.CustomerId,
                    Amount = order.Total,
                    Currency = currency
                };
                reservation = new OrderReservation
                {
                    Id = order.OrderId,
                    OrderId = order.OrderId,
                    CustomerId = order.CustomerId,
                    Status = ReservationStatus.Reserved,
                    Lines = reserved,
                    HasPendingReleases = false,
                    Outbox = BuildOutbox(outboundEventId, nameof(InventoryReservedEvent), topic, payload, correlationId, traceParent, now),
                    CreatedAt = now,
                    LastUpdated = now
                };
            }
            else
            {
                // Partial failure: the lines we DID reserve must be released (locally — no other
                // service has acted yet), so mark them PendingRelease for the worker and still emit the
                // failure so OrderService can cancel.
                foreach (var line in reserved)
                {
                    line.Status = ReservationLineStatus.PendingRelease;
                }

                var payload = new InventoryReservationFailedEvent
                {
                    EventId = outboundEventId,
                    OrderId = order.OrderId,
                    Reason = failureReason
                };
                reservation = new OrderReservation
                {
                    Id = order.OrderId,
                    OrderId = order.OrderId,
                    CustomerId = order.CustomerId,
                    Status = ReservationStatus.Failed,
                    Lines = reserved,
                    HasPendingReleases = reserved.Count > 0,
                    Outbox = BuildOutbox(outboundEventId, nameof(InventoryReservationFailedEvent), topic, payload, correlationId, traceParent, now),
                    CreatedAt = now,
                    LastUpdated = now
                };
            }

            await _reservations.UpsertAsync(reservation, cancellationToken);
            await MarkProcessedAsync(order.EventId, cancellationToken);

            _logger.LogInformation(
                "Order {OrderId} reservation {Result}; outbox event {EventId} ({EventType}) queued for '{Topic}'",
                order.OrderId, allReserved ? "succeeded" : "failed", outboundEventId, reservation.Outbox!.EventType, topic);
        }

        public async Task ReleaseForCancelAsync(OrderCancelledIntegrationEvent cancelled, CancellationToken cancellationToken = default)
        {
            if (cancelled is null) throw new ArgumentNullException(nameof(cancelled));

            if (cancelled.EventId != Guid.Empty && await _inbox.HasBeenProcessedAsync(cancelled.EventId, cancellationToken))
            {
                _logger.LogInformation("Duplicate OrderCancelled {EventId} — already processed, skipping", cancelled.EventId);
                return;
            }

            var reservation = await _reservations.GetByOrderIdAsync(cancelled.OrderId, cancellationToken);
            if (reservation is null)
            {
                // Nothing was reserved for this order (or it already failed) — nothing to compensate.
                _logger.LogInformation("OrderCancelled {OrderId}: no reservation found, nothing to release", cancelled.OrderId);
                await MarkProcessedAsync(cancelled.EventId, cancellationToken);
                return;
            }

            if (reservation.Status == ReservationStatus.Reserved)
            {
                foreach (var line in reservation.Lines)
                {
                    if (line.Status == ReservationLineStatus.Reserved)
                    {
                        line.Status = ReservationLineStatus.PendingRelease;
                    }
                }
                reservation.Status = ReservationStatus.Cancelled;
                reservation.HasPendingReleases = true;
                reservation.LastUpdated = DateTime.UtcNow;
                await _reservations.UpsertAsync(reservation, cancellationToken);
                _logger.LogInformation("OrderCancelled {OrderId}: reserved stock flagged for release", cancelled.OrderId);
            }

            await MarkProcessedAsync(cancelled.EventId, cancellationToken);
        }

        private Task MarkProcessedAsync(Guid eventId, CancellationToken cancellationToken) =>
            eventId != Guid.Empty ? _inbox.MarkProcessedAsync(eventId, cancellationToken) : Task.CompletedTask;

        private static ReservationOutbox BuildOutbox(
            Guid eventId, string eventType, string topic, object payload,
            string? correlationId, string? traceParent, DateTime now) =>
            new()
            {
                EventId = eventId,
                EventType = eventType,
                Topic = topic,
                Status = OutboxStatus.Pending,
                CorrelationId = correlationId,
                TraceParent = traceParent,
                // Serialize by runtime type — `payload` is `object`, and Serialize<object> would emit {}.
                Payload = JsonSerializer.Serialize(payload, payload.GetType()),
                RetryCount = 0,
                CreatedAt = now
            };
    }
}
