namespace BookStore.AiService.Core.Abstractions;

/// <summary>
/// Ingestion side: keeps BookEmbeddings in sync with ProductService's catalog. Transport-free
/// (unit-testable) and defined in Core so the Infrastructure subscriber can depend on it without
/// depending on Application; implemented in Application.
/// </summary>
public interface IBookIndexService
{
    Task IndexProductAsync(
        Guid eventId, Guid productId, string name, string description, string category, decimal price,
        CancellationToken cancellationToken = default);

    Task RemoveProductAsync(Guid eventId, Guid productId, CancellationToken cancellationToken = default);
}
