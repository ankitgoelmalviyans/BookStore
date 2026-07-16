using BookStore.OrderService.Application.Dtos;

namespace BookStore.OrderService.Application.Abstractions;

/// <summary>
/// The read-side (query) contract. Returns denormalised DTOs shaped for display, never domain
/// entities — the CQRS read model, kept separate from the command path.
/// </summary>
public interface IOrderQueries
{
    Task<OrderDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummaryDto>> GetHistoryAsync(string customerId, CancellationToken cancellationToken = default);
}
