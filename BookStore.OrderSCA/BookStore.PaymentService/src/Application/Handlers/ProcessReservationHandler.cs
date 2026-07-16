using System.Text.Json;
using BookStore.PaymentService.Core.Abstractions;
using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Enums;
using BookStore.PaymentService.Core.Events;
using BookStore.PaymentService.Core.Messaging;
using BookStore.PaymentService.Core.Payments;
using BookStore.PaymentService.Core.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BookStore.PaymentService.Application.Handlers;

/// <summary>
/// Charges for a reserved order and records the outcome. The heart of PaymentService's saga role:
/// dedupe → charge (idempotency-keyed) → persist payment + outcome event + inbox marker atomically.
/// </summary>
public class ProcessReservationHandler : IProcessReservationHandler
{
    private readonly IInboxStore _inbox;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentRepository _repository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProcessReservationHandler> _logger;

    public ProcessReservationHandler(
        IInboxStore inbox,
        IPaymentGateway gateway,
        IPaymentRepository repository,
        IConfiguration configuration,
        ILogger<ProcessReservationHandler> logger)
    {
        _inbox = inbox;
        _gateway = gateway;
        _repository = repository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ReservationHandlingOutcome> HandleAsync(
        InventoryReservedEvent reserved,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default)
    {
        if (reserved is null)
        {
            throw new ArgumentNullException(nameof(reserved));
        }

        // Dedup/idempotency identity: the event id when the producer stamped one, else the order id
        // (an order is charged at most once) — so an unversioned producer can't cause a double charge
        // and can't collide on an empty inbox key.
        var dedupeKey = reserved.EventId != Guid.Empty ? reserved.EventId : reserved.OrderId;

        // Inbox pre-check: skip a redelivered message rather than charging again.
        if (await _inbox.HasBeenProcessedAsync(dedupeKey, cancellationToken))
        {
            _logger.LogInformation(
                "Duplicate InventoryReserved {DedupeKey} for order {OrderId} — already processed, skipping",
                dedupeKey, reserved.OrderId);
            return ReservationHandlingOutcome.Duplicate;
        }

        var currency = !string.IsNullOrWhiteSpace(reserved.Currency)
            ? reserved.Currency
            : (_configuration["Payments:Currency"] ?? "usd");
        var paymentMethod = _configuration["Payments:DefaultPaymentMethod"] ?? "pm_card_visa";
        var outboundTopic = _configuration["AzureServiceBus:OutboundTopic"] ?? "payment-events";

        // The dedupe key is also the gateway idempotency key: even if two deliveries race past the
        // inbox check, the gateway returns the original charge instead of creating a second one.
        var charge = await _gateway.ChargeAsync(
            new ChargeRequest(reserved.OrderId, reserved.Amount, currency, dedupeKey.ToString(), paymentMethod),
            cancellationToken);

        var paymentId = Guid.NewGuid();
        var outboundEventId = Guid.NewGuid();

        var payment = new Payment
        {
            Id = paymentId,
            OrderId = reserved.OrderId,
            CustomerId = reserved.CustomerId,
            Amount = reserved.Amount,
            Currency = currency,
            Status = charge.Succeeded ? PaymentStatus.Captured : PaymentStatus.Failed,
            ProviderPaymentId = charge.ProviderPaymentId,
            FailureReason = charge.Succeeded ? null : charge.FailureReason,
            CreatedAt = DateTime.UtcNow
        };

        OutboxMessage outbox;
        if (charge.Succeeded)
        {
            var payload = new PaymentProcessedEvent
            {
                EventId = outboundEventId,
                OrderId = reserved.OrderId,
                PaymentId = paymentId,
                Amount = reserved.Amount,
                ProviderPaymentId = charge.ProviderPaymentId
            };
            outbox = BuildOutbox(outboundEventId, nameof(PaymentProcessedEvent), outboundTopic, payload, correlationId, traceParent);
        }
        else
        {
            var payload = new PaymentFailedEvent
            {
                EventId = outboundEventId,
                OrderId = reserved.OrderId,
                PaymentId = paymentId,
                Reason = charge.FailureReason ?? "Payment declined"
            };
            outbox = BuildOutbox(outboundEventId, nameof(PaymentFailedEvent), outboundTopic, payload, correlationId, traceParent);
        }

        // Payment row + outcome outbox event + inbox marker committed together, one transaction.
        await _repository.SaveChargeAsync(payment, outbox, dedupeKey, cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} charge {Result}; payment {PaymentId}, outbox event {EventId} ({EventType}) queued for '{Topic}'",
            reserved.OrderId, charge.Succeeded ? "captured" : "declined", paymentId, outboundEventId, outbox.EventType, outboundTopic);

        return charge.Succeeded ? ReservationHandlingOutcome.Charged : ReservationHandlingOutcome.Declined;
    }

    private static OutboxMessage BuildOutbox(
        Guid eventId, string eventType, string topic, object payload, string? correlationId, string? traceParent) =>
        new()
        {
            EventId = eventId,
            EventType = eventType,
            Topic = topic,
            Status = OutboxMessage.Pending,
            CorrelationId = correlationId,
            TraceParent = traceParent,
            // Serialize by RUNTIME type: `payload` is typed `object` here, and Serialize<object> would
            // emit an empty document. GetType() serializes the concrete event.
            Payload = JsonSerializer.Serialize(payload, payload.GetType()),
            CreatedAt = DateTime.UtcNow
        };
}
