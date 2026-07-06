namespace BookStore.ProductService.Core.Messaging;

public interface IMessagePublisher
{
    /// <param name="correlationId">
    /// Explicit CorrelationId to stamp on the message. Supplied by the outbox publisher (which runs
    /// outside an HTTP request, so there is no ambient <c>HttpContext</c> to read it from). When
    /// null, the implementation falls back to the current request's CorrelationId, then a new GUID.
    /// </param>
    /// <param name="traceParent">
    /// Optional W3C <c>traceparent</c> of the originating operation (captured at the HTTP create and
    /// stored on the outbox record). When supplied, the publish span joins that trace so the whole
    /// create → outbox → publish → consume chain shares one TraceId. When null, the publish starts a
    /// new root trace.
    /// </param>
    Task PublishAsync<T>(T message, string topic, string? correlationId = null, string? traceParent = null) where T : class;
}
