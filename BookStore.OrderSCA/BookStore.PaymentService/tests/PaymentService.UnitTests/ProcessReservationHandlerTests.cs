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
}
