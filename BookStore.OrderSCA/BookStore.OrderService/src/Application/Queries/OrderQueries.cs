using BookStore.OrderService.Application.Abstractions;
using BookStore.OrderService.Application.Dtos;
using BookStore.OrderService.Core.Repositories;

namespace BookStore.OrderService.Application.Queries;

/// <summary>
/// Read-side (query) service. Maps order aggregates to display DTOs. Today it reads through the same
/// <see cref="IOrderRepository"/> as the write side; a denormalised <c>OrderSummary</c> projection
/// table is the documented next optimisation (docs/ROADMAP.md) when read volume warrants it — the
/// interface already isolates callers from that change.
/// </summary>
public class OrderQueries : IOrderQueries
{
    private readonly IOrderRepository _repository;

    public OrderQueries(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<OrderDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _repository.GetByIdAsync(id, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return new OrderDetailDto
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            Status = order.Status.ToString(),
            Total = order.Total,
            CreatedAt = order.CreatedAt,
            Items = order.Items
                .Select(i => new OrderItemDto
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                })
                .ToList()
        };
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetHistoryAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var orders = await _repository.GetByCustomerAsync(customerId, cancellationToken);

        return orders
            .Select(o => new OrderSummaryDto
            {
                Id = o.Id,
                Status = o.Status.ToString(),
                Total = o.Total,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt
            })
            .ToList();
    }
}
