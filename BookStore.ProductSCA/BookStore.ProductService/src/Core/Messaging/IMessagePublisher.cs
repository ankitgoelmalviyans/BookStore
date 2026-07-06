namespace BookStore.ProductService.Core.Messaging;

public interface IMessagePublisher
{
    /// <param name="correlationId">
    /// Explicit CorrelationId to stamp on the message. Supplied by the outbox publisher (which runs
    /// outside an HTTP request, so there is no ambient <c>HttpContext</c> to read it from). When
    /// null, the implementation falls back to the current request's CorrelationId, then a new GUID.
    /// </param>
    Task PublishAsync<T>(T message, string topic, string? correlationId = null) where T : class;
}
