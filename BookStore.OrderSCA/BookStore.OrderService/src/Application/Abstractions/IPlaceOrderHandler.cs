using BookStore.OrderService.Application.Commands;

namespace BookStore.OrderService.Application.Abstractions;

/// <summary>
/// The write-side (command) handler. Separated from the read side (<see cref="IOrderQueries"/>) to
/// keep the CQRS split explicit: commands enforce invariants and mutate state; queries never do.
/// </summary>
public interface IPlaceOrderHandler
{
    /// <returns>The id of the newly placed order (status <c>Pending</c>).</returns>
    Task<Guid> HandleAsync(
        PlaceOrderCommand command,
        string? correlationId = null,
        string? traceParent = null,
        CancellationToken cancellationToken = default);
}
