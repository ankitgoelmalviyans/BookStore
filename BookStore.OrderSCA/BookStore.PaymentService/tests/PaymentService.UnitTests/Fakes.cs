using BookStore.PaymentService.Core.Entities;
using BookStore.PaymentService.Core.Enums;
using BookStore.PaymentService.Core.Messaging;
using BookStore.PaymentService.Core.Payments;
using BookStore.PaymentService.Core.Repositories;

namespace BookStore.PaymentService.UnitTests;

/// <summary>
/// Records what the repository was asked to persist across the pending/confirm phases. Mirrors the
/// real conditional-update semantics: <see cref="TryClaimAndConfirmAsync"/> and
/// <see cref="MarkCancelledIfPendingAsync"/> only mutate <see cref="SavedPayment"/> if it's currently
/// Pending, same as the real repository's <c>WHERE Status = Pending</c> update.
/// </summary>
internal sealed class FakePaymentRepository : IPaymentRepository
{
    public Payment? SavedPayment { get; private set; }
    public OutboxMessage? SavedOutbox { get; private set; }
    public Guid SavedInboxEventId { get; private set; }
    public int PendingSaveCallCount { get; private set; }
    public int ConfirmationSaveCallCount { get; private set; }
    public int CancelCallCount { get; private set; }

    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(SavedPayment?.OrderId == orderId ? SavedPayment : null);

    public Task SavePendingAsync(Payment payment, Guid inboxEventId, CancellationToken cancellationToken = default)
    {
        PendingSaveCallCount++;
        SavedPayment = payment;
        SavedInboxEventId = inboxEventId;
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimAndConfirmAsync(
        Guid paymentId, PaymentStatus status, string? providerPaymentId, string? failureReason,
        OutboxMessage outbox, CancellationToken cancellationToken = default)
    {
        if (SavedPayment is null || SavedPayment.Id != paymentId || SavedPayment.Status != PaymentStatus.Pending)
        {
            return Task.FromResult(false);
        }

        ConfirmationSaveCallCount++;
        SavedPayment.Status = status;
        SavedPayment.ProviderPaymentId = providerPaymentId;
        SavedPayment.FailureReason = failureReason;
        SavedOutbox = outbox;
        return Task.FromResult(true);
    }

    public Task MarkCancelledIfPendingAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        if (SavedPayment?.OrderId == orderId && SavedPayment.Status == PaymentStatus.Pending)
        {
            CancelCallCount++;
            SavedPayment.Status = PaymentStatus.Failed;
            SavedPayment.FailureReason = reason;
        }
        return Task.CompletedTask;
    }
}

/// <summary>Inbox stub whose "already processed" answer is set per test.</summary>
internal sealed class FakeInboxStore : IInboxStore
{
    private readonly bool _alreadyProcessed;

    public FakeInboxStore(bool alreadyProcessed) => _alreadyProcessed = alreadyProcessed;

    public Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_alreadyProcessed);
}

/// <summary>Gateway stub returning a fixed result, and recording the request it received. An
/// optional callback runs during the charge — models something else (e.g. a cancellation) landing
/// in the real window between the charge and the atomic claim of its outcome.</summary>
internal sealed class StubPaymentGateway : IPaymentGateway
{
    private readonly ChargeResult _result;
    private readonly Action? _duringCharge;

    public StubPaymentGateway(ChargeResult result, Action? duringCharge = null)
    {
        _result = result;
        _duringCharge = duringCharge;
    }

    public ChargeRequest? LastRequest { get; private set; }

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        _duringCharge?.Invoke();
        return Task.FromResult(_result);
    }
}
