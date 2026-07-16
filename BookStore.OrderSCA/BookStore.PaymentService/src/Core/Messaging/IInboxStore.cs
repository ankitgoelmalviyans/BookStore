namespace BookStore.PaymentService.Core.Messaging;

/// <summary>
/// Read side of the inbox: has this inbound event id already been processed? The corresponding
/// <em>write</em> (marking it processed) is done inside the payment repository's atomic transaction,
/// not here — so on SQL we get "charge + outbox event + processed marker" in one commit, strictly
/// better than InventoryService's two-step mark (which Cosmos forced). See
/// <c>IPaymentRepository.SaveChargeAsync</c>.
/// </summary>
public interface IInboxStore
{
    Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);
}
