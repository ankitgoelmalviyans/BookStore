namespace BookStore.OrderService.Core.Messaging;

/// <summary>
/// Read side of the inbox: has this inbound event id already been processed? The write (marking it
/// processed) happens inside the repository's atomic outcome transaction, not here — so the order's
/// state change and the processed marker commit together. See <c>IOrderRepository.SaveOutcomeAsync</c>.
/// </summary>
public interface IInboxStore
{
    Task<bool> HasBeenProcessedAsync(Guid eventId, CancellationToken cancellationToken = default);
}
