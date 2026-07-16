using System.Text.Json;
using BookStore.PaymentService.Application.Handlers;
using BookStore.PaymentService.Core.Abstractions;
using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Enums;
using BookStore.PaymentService.Core.Events;
using BookStore.PaymentService.Core.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookStore.PaymentService.UnitTests;

public class ProcessReservationHandlerTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureServiceBus:OutboundTopic"] = "payment-events",
                ["Payments:DefaultPaymentMethod"] = "pm_card_visa"
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
    public async Task Approved_charge_persists_payment_and_PaymentProcessed_event()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_123"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();

        var outcome = await handler.HandleAsync(reserved);

        Assert.Equal(ReservationHandlingOutcome.Charged, outcome);
        Assert.Equal(1, repo.SaveCallCount);
        Assert.Equal(PaymentStatus.Captured, repo.SavedPayment!.Status);
        Assert.Equal("pi_123", repo.SavedPayment!.ProviderPaymentId);
        Assert.Equal(nameof(PaymentProcessedEvent), repo.SavedOutbox!.EventType);
        Assert.Equal("payment-events", repo.SavedOutbox!.Topic);

        var payload = JsonSerializer.Deserialize<PaymentProcessedEvent>(repo.SavedOutbox!.Payload);
        Assert.Equal(reserved.OrderId, payload!.OrderId);
        Assert.Equal(reserved.Amount, payload.Amount);
    }

    [Fact]
    public async Task Declined_charge_persists_failed_payment_and_PaymentFailed_event()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Failure("card_declined"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();

        var outcome = await handler.HandleAsync(reserved);

        Assert.Equal(ReservationHandlingOutcome.Declined, outcome);
        Assert.Equal(PaymentStatus.Failed, repo.SavedPayment!.Status);
        Assert.Equal("card_declined", repo.SavedPayment!.FailureReason);
        Assert.Equal(nameof(PaymentFailedEvent), repo.SavedOutbox!.EventType);

        var payload = JsonSerializer.Deserialize<PaymentFailedEvent>(repo.SavedOutbox!.Payload);
        Assert.Equal(reserved.OrderId, payload!.OrderId);
        Assert.Equal("card_declined", payload.Reason);
    }

    [Fact]
    public async Task Transient_gateway_fault_throws_and_persists_nothing()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.TransientError("gateway_unavailable"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);

        // A transient fault must NOT record PaymentFailed or mark the message processed — it throws so
        // the subscriber abandons the message for redelivery.
        await Assert.ThrowsAsync<TransientPaymentException>(() => handler.HandleAsync(Reserved()));
        Assert.Equal(0, repo.SaveCallCount);
    }

    [Fact]
    public async Task Duplicate_event_is_skipped_without_charging()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_should_not_be_used"));
        var handler = CreateHandler(new FakeInboxStore(alreadyProcessed: true), gateway, repo);

        var outcome = await handler.HandleAsync(Reserved());

        Assert.Equal(ReservationHandlingOutcome.Duplicate, outcome);
        Assert.Equal(0, repo.SaveCallCount);
        Assert.Null(gateway.LastRequest);
    }

    [Fact]
    public async Task Idempotency_key_and_inbox_key_are_the_inbound_event_id()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_123"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();

        await handler.HandleAsync(reserved);

        Assert.Equal(reserved.EventId.ToString(), gateway.LastRequest!.IdempotencyKey);
        Assert.Equal(reserved.EventId, repo.SavedInboxEventId);
    }

    [Fact]
    public async Task Missing_event_id_falls_back_to_order_id_for_dedup_and_idempotency()
    {
        var repo = new FakePaymentRepository();
        var gateway = new StubPaymentGateway(ChargeResult.Success("pi_123"));
        var handler = CreateHandler(new FakeInboxStore(false), gateway, repo);
        var reserved = Reserved();
        reserved.EventId = Guid.Empty;

        await handler.HandleAsync(reserved);

        Assert.Equal(reserved.OrderId.ToString(), gateway.LastRequest!.IdempotencyKey);
        Assert.Equal(reserved.OrderId, repo.SavedInboxEventId);
    }
}
