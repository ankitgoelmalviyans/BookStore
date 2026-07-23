using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Entities;
using Microsoft.Extensions.Logging;

namespace BookStore.AiService.Application.Services;

/// <summary>
/// Ingestion side of RAG: turns a product's description into an embedding and keeps BookEmbeddings in
/// sync with ProductService's catalog (create/update = re-embed and upsert, delete = remove).
/// </summary>
public class BookIndexService : IBookIndexService
{
    private readonly IBookEmbeddingStore _store;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly IInboxStore _inbox;
    private readonly ILogger<BookIndexService> _logger;

    public BookIndexService(
        IBookEmbeddingStore store, IEmbeddingClient embeddingClient, IInboxStore inbox, ILogger<BookIndexService> logger)
    {
        _store = store;
        _embeddingClient = embeddingClient;
        _inbox = inbox;
        _logger = logger;
    }

    public async Task IndexProductAsync(
        Guid eventId, Guid productId, string name, string description, string category, decimal price,
        CancellationToken cancellationToken = default)
    {
        if (eventId != Guid.Empty && await _inbox.HasBeenProcessedAsync(eventId, cancellationToken))
        {
            _logger.LogInformation("Duplicate product event {EventId} — already processed, skipping", eventId);
            return;
        }

        var embedding = await _embeddingClient.EmbedAsync(description, cancellationToken);

        await _store.UpsertAsync(new BookEmbeddingRecord
        {
            Id = productId.ToString(),
            ProductId = productId,
            Name = name,
            Description = description,
            Category = category,
            Price = price,
            Embedding = embedding
        }, cancellationToken);

        if (eventId != Guid.Empty)
        {
            await _inbox.MarkProcessedAsync(eventId, cancellationToken);
        }

        _logger.LogInformation("Indexed product {ProductId} ({Name})", productId, name);
    }

    public async Task RemoveProductAsync(Guid eventId, Guid productId, CancellationToken cancellationToken = default)
    {
        if (eventId != Guid.Empty && await _inbox.HasBeenProcessedAsync(eventId, cancellationToken))
        {
            _logger.LogInformation("Duplicate product event {EventId} — already processed, skipping", eventId);
            return;
        }

        await _store.DeleteAsync(productId, cancellationToken);

        if (eventId != Guid.Empty)
        {
            await _inbox.MarkProcessedAsync(eventId, cancellationToken);
        }

        _logger.LogInformation("Removed product {ProductId} from the index", productId);
    }
}
