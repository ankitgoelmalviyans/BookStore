using System.Text.Json;
using BookStore.PaymentService.Application.Handlers;
using BookStore.PaymentService.Core.Abstractions;
using BookStore.PaymentService.Core.Enums;
using BookStore.PaymentService.Core.Events;
using BookStore.PaymentService.Core.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookStore.PaymentService.UnitTests;

public class ProcessReservationHandlerTests
{
    private const string PaymentMethodId = "pm_card_visa";

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureServiceBus:OutboundTopic"] = "payment-events"
            })
            .Build();

    private static InventoryReservedEvent Reserved() => new()
    {
        EventId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        CustomerId = "customer-1",
        Amount = 25.00m,
        Currency = "usd"
    };

    private static ProcessReservationHandler CreateHandler(
        FakeInboxStore inbox, StubPaymentGateway gateway, FakePaymentRepository repo) =>
        new(inbox, gateway, repo, Config(), NullLogger<ProcessReservationHandler>.Instance);

    [Fact]
    public async Task RecordPendingAsync_persists_a_pending_payment_without_charging()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_should_not_be_used"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();

        var outcome = await handler.RecordPendingAsync(reserved);

        Assert.Equal(ReservationHandlingOutcome.Recorded, outcome);
        Assert.Equal(1, repo.PendingSaveCallCount);
        Assert.Equal(PaymentStatus.Pending, repo.SavedPayment!.Status);
        Assert.Equal(reserved.OrderId, repo.SavedPayment!.OrderId);
        Assert.Null(gateway.LastRequest); // no charge attempted yet
    }

    [Fact]
    public async Task RecordPendingAsync_duplicate_event_is_skipped()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_123"));
        var handler = CreateHandler(new FakeInboxStore(alreadyProcessed: true), gateway, repo);

        var outcome = await handler.RecordPendingAsync(Reserved());

        Assert.Equal(ReservationHandlingOutcome.Duplicate, outcome);
        Assert.Equal(0, repo.PendingSaveCallCount);
    }

    [Fact]
    public async Task ConfirmAsync_with_no_pending_payment_returns_NotFound()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_123"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);

        var outcome = await handler.ConfirmAsync(Guid.NewGuid(), PaymentMethodId);

        Assert.Equal(ConfirmationOutcome.NotFound, outcome);
        Assert.Null(gateway.LastRequest);
    }

    [Fact]
    public async Task ConfirmAsync_approved_charge_captures_and_queues_PaymentProcessed()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_123"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();
        await handler.RecordPendingAsync(reserved);

        var outcome = await handler.ConfirmAsync(reserved.OrderId, PaymentMethodId);

        Assert.Equal(ConfirmationOutcome.Charged, outcome);
        Assert.Equal(1, repo.ConfirmationSaveCallCount);
        Assert.Equal(PaymentStatus.Captured, repo.SavedPayment!.Status);
        Assert.Equal("pi_123", repo.SavedPayment!.ProviderPaymentId);
        Assert.Equal(PaymentMethodId, gateway.LastRequest!.PaymentMethodToken);
        Assert.Equal(nameof(PaymentProcessedEvent), repo.SavedOutbox!.EventType);
        Assert.Equal("payment-events", repo.SavedOutbox!.Topic);

        var payload = JsonSerializer.Deserialize<PaymentProcessedEvent>(repo.SavedOutbox!.Payload);
        Assert.Equal(reserved.OrderId, payload!.OrderId);
        Assert.Equal(reserved.Amount, payload.Amount);
    }

    [Fact]
    public async Task ConfirmAsync_declined_charge_fails_and_queues_PaymentFailed()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Failure("card_declined"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();
        await handler.RecordPendingAsync(reserved);

        var outcome = await handler.ConfirmAsync(reserved.OrderId, PaymentMethodId);

        Assert.Equal(ConfirmationOutcome.Declined, outcome);
        Assert.Equal(PaymentStatus.Failed, repo.SavedPayment!.Status);
        Assert.Equal("card_declined", repo.SavedPayment!.FailureReason);
        Assert.Equal(nameof(PaymentFailedEvent), repo.SavedOutbox!.EventType);

        var payload = JsonSerializer.Deserialize<PaymentFailedEvent>(repo.SavedOutbox!.Payload);
        Assert.Equal(reserved.OrderId, payload!.OrderId);
        Assert.Equal("card_declined", payload.Reason);
    }

    [Fact]
    public async Task ConfirmAsync_transient_gateway_fault_leaves_payment_pending_for_retry()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.TransientError("gateway_unavailable"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();
        await handler.RecordPendingAsync(reserved);

        var outcome = await handler.ConfirmAsync(reserved.OrderId, PaymentMethodId);

        // Must NOT record PaymentFailed and must NOT consume the Pending row — the customer can
        // retry the Pay action.
        Assert.Equal(ConfirmationOutcome.TransientError, outcome);
        Assert.Equal(0, repo.ConfirmationSaveCallCount);
        Assert.Equal(PaymentStatus.Pending, repo.SavedPayment!.Status);
    }

    [Fact]
    public async Task ConfirmAsync_idempotency_key_is_the_payment_id()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_123"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();
        await handler.RecordPendingAsync(reserved);
        var paymentId = repo.SavedPayment!.Id;

        await handler.ConfirmAsync(reserved.OrderId, PaymentMethodId);

        Assert.Equal(paymentId.ToString(), gateway.LastRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task HandleOrderCancelledAsync_resolves_a_pending_payment_as_failed()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_should_not_be_used"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();
        await handler.RecordPendingAsync(reserved);

        await handler.HandleOrderCancelledAsync(new OrderCancelledEvent { EventId = Guid.NewGuid(), OrderId = reserved.OrderId });

        Assert.Equal(1, repo.CancelCallCount);
        Assert.Equal(PaymentStatus.Failed, repo.SavedPayment!.Status);
    }

    [Fact]
    public async Task HandleOrderCancelledAsync_is_a_noop_for_an_already_resolved_payment()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_123"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();
        await handler.RecordPendingAsync(reserved);
        await handler.ConfirmAsync(reserved.OrderId, PaymentMethodId); // already Captured

        await handler.HandleOrderCancelledAsync(new OrderCancelledEvent { EventId = Guid.NewGuid(), OrderId = reserved.OrderId });

        Assert.Equal(0, repo.CancelCallCount); // conditional update found it wasn't Pending — didn't touch it
        Assert.Equal(PaymentStatus.Captured, repo.SavedPayment!.Status); // unchanged
    }

    [Fact]
    public async Task ConfirmAsync_loses_the_race_to_a_cancellation_and_reports_AlreadyResolved()
    {
        // Simulates the exact race the atomic claim exists for: ConfirmAsync's early read sees
        // Pending and proceeds to charge, but the order is cancelled (resolving the payment Failed)
        // WHILE the gateway call is in flight — a real, non-instant step. The atomic claim at the
        // end must see the row is no longer Pending and refuse to commit, rather than overwriting
        // the cancellation's Failed with its own Charged.
        var repo = new FakePaymentRepository();
        var reserved = Reserved();
        var gateway = new StubPaymentGateway(
            ChargeResult.Success("pi_123"),
            duringCharge: () => repo.MarkCancelledIfPendingAsync(reserved.OrderId, "Order cancelled by customer").GetAwaiter().GetResult());
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        await handler.RecordPendingAsync(reserved);

        var outcome = await handler.ConfirmAsync(reserved.OrderId, PaymentMethodId);

        Assert.Equal(ConfirmationOutcome.AlreadyResolved, outcome);
        Assert.Equal(PaymentStatus.Failed, repo.SavedPayment!.Status); // still the cancellation's Failed, not overwritten to Captured
    }
}
