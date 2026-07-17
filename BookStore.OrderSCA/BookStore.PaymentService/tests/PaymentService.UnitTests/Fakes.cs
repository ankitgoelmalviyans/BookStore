using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Messaging;
using BookStore.PaymentService.Core.Payments;
using BookStore.PaymentService.Core.Repositories;

namespace BookStore.PaymentService.UnitTests;

/// <summary>Records the charge the repository was asked to persist (payment + outbox + inbox key).</summary>
internal sealed class FakePaymentRepository : IPaymentRepository
{
    public Payment? SavedPayment { get; private set; }
    public OutboxMessage? SavedOutbox { get; private set; }
    public Guid SavedInboxEventId { get; private set; }
    public int SaveCallCount { get; private set; }

    public Task SaveChargeAsync(Payment payment, OutboxMessage outbox, Guid inboxEventId, CancellationToken cancellationToken = default)
    {
        SaveCallCount++;
        SavedPayment = payment;
        SavedOutbox = outbox;
        SavedInboxEventId = inboxEventId;
        return Task.CompletedTask;
    }

    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(SavedPayment?.OrderId == orderId ? SavedPayment : null);
}

/// <summary>Inbox stub whose "already processed" answer is set per test.</summary>
internal sealed class FakeInboxStore : IInboxStore
{
    private readonly bool _alreadyProcessed;

    public FakeInboxStore(bool alreadyProcessed) => _alreadyProcessed = alreadyProcessed;

    public Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_alreadyProcessed);
}

/// <summary>Gateway stub returning a fixed result, and recording the request it received.</summary>
internal sealed class StubPaymentGateway : IPaymentGateway
{
    private readonly ChargeResult _result;

    public StubPaymentGateway(ChargeResult result) => _result = result;

    public ChargeRequest? LastRequest { get; private set; }

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(_result);
    }
}
