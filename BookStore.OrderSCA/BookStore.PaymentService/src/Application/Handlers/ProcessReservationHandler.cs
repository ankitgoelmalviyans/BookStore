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
/// Handles the two saga-adjacent payment actions. <see cref="RecordPendingAsync"/> reacts to an
/// inbound <see cref="InventoryReservedEvent"/> by recording a Pending payment row — no charge yet,
/// holding for the customer's explicit pay/cancel instead of charging automatically.
/// <see cref="ConfirmAsync"/> is the customer-initiated "Pay" action: charge → update that same row →
/// outcome outbox event.
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

    public async Task<ReservationHandlingOutcome> RecordPendingAsync(
        InventoryReservedEvent reserved,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default)
    {
        if (reserved is null)
        {
            throw new ArgumentNullException(nameof(reserved));
        }

        var dedupeKey = reserved.EventId != Guid.Empty ? reserved.EventId : reserved.OrderId;

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

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = reserved.OrderId,
            CustomerId = reserved.CustomerId,
            Amount = reserved.Amount,
            Currency = currency,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.SavePendingAsync(payment, dedupeKey, cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} reserved — payment {PaymentId} recorded Pending, awaiting customer pay/cancel",
            reserved.OrderId, payment.Id);

        return ReservationHandlingOutcome.Recorded;
    }

    public async Task<ConfirmationOutcome> ConfirmAsync(
        Guid orderId,
        string paymentMethodId,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default)
    {
        // Early, non-authoritative check — avoids charging a card for an order that's already
        // resolved in the common case. The AUTHORITATIVE check is the conditional update below;
        // this one just saves a gateway round-trip when it's obviously already too late.
        var payment = await _repository.GetByOrderIdAsync(orderId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Pending)
        {
            return ConfirmationOutcome.NotFound;
        }

        var outboundTopic = _configuration["AzureServiceBus:OutboundTopic"] ?? "payment-events";

        // Idempotency key = the payment's own id: a retried confirm (e.g. a double-click, or a retry
        // after a transient error) for the same Pending payment returns the original charge from the
        // gateway instead of creating a second one.
        var charge = await _gateway.ChargeAsync(
            new ChargeRequest(orderId, payment.Amount, payment.Currency, payment.Id.ToString(), paymentMethodId),
            cancellationToken);

        if (!charge.Succeeded && charge.Retryable)
        {
            _logger.LogWarning(
                "Transient payment gateway error for order {OrderId}: {Reason} — payment stays Pending for retry",
                orderId, charge.FailureReason);
            return ConfirmationOutcome.TransientError;
        }

        var outboundEventId = Guid.NewGuid();
        var newStatus = charge.Succeeded ? PaymentStatus.Captured : PaymentStatus.Failed;

        OutboxMessage outbox;
        if (charge.Succeeded)
        {
            var payload = new PaymentProcessedEvent
            {
                EventId = outboundEventId,
                OrderId = orderId,
                PaymentId = payment.Id,
                Amount = payment.Amount,
                ProviderPaymentId = charge.ProviderPaymentId
            };
            outbox = BuildOutbox(outboundEventId, nameof(PaymentProcessedEvent), outboundTopic, payload, correlationId, traceParent);
        }
        else
        {
            var payload = new PaymentFailedEvent
            {
                EventId = outboundEventId,
                OrderId = orderId,
                PaymentId = payment.Id,
                Reason = charge.FailureReason ?? "Payment declined"
            };
            outbox = BuildOutbox(outboundEventId, nameof(PaymentFailedEvent), outboundTopic, payload, correlationId, traceParent);
        }

        // Atomic claim: commits the Status/outbox together, but only if the payment is STILL Pending
        // right now — closing the race a plain tracked update would miss (a concurrent Confirm, or
        // HandleOrderCancelledAsync resolving it first). If we lose the race, the gateway charge
        // above already happened and isn't undone here — see ConfirmationOutcome.AlreadyResolved.
        var claimed = await _repository.TryClaimAndConfirmAsync(
            payment.Id, newStatus, charge.ProviderPaymentId, charge.Succeeded ? null : charge.FailureReason, outbox, cancellationToken);

        if (!claimed)
        {
            _logger.LogWarning(
                "Order {OrderId} payment {PaymentId} was no longer Pending when the charge tried to commit — " +
                "a concurrent confirm or cancellation resolved it first",
                orderId, payment.Id);
            return ConfirmationOutcome.AlreadyResolved;
        }

        _logger.LogInformation(
            "Order {OrderId} charge {Result}; payment {PaymentId}, outbox event {EventId} ({EventType}) queued for '{Topic}'",
            orderId, charge.Succeeded ? "captured" : "declined", payment.Id, outboundEventId, outbox.EventType, outboundTopic);

        return charge.Succeeded ? ConfirmationOutcome.Charged : ConfirmationOutcome.Declined;
    }

    public async Task HandleOrderCancelledAsync(
        OrderCancelledEvent cancelled,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default)
    {
        if (cancelled is null)
        {
            throw new ArgumentNullException(nameof(cancelled));
        }

        // No inbox dedup needed: MarkCancelledIfPendingAsync's conditional update is naturally
        // idempotent — a redelivery finds the payment already non-Pending and does nothing.
        await _repository.MarkCancelledIfPendingAsync(cancelled.OrderId, "Order cancelled by customer", cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} cancelled — any Pending payment for it resolved Failed so it can no longer be charged",
            cancelled.OrderId);
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
