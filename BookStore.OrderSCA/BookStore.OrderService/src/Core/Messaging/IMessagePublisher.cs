namespace BookStore.OrderService.Core.Messaging;

public interface IMessagePublisher
{
    /// <param name="correlationId">
    /// Explicit CorrelationId to stamp on the message. Supplied by the outbox publisher (which runs
    /// outside an HTTP request, so there is no ambient <c>HttpContext</c> to read it from). When
    /// null, the implementation falls back to the current request's CorrelationId, then a new GUID.
    /// </param>
    /// <param name="traceParent">
    /// Optional W3C <c>traceparent</c> of the originating operation (captured at the HTTP place-order
    /// and stored on the outbox record). When supplied, the publish span joins that trace so the whole
    /// place → outbox → publish → consume chain shares one TraceId. When null, a new root trace starts.
    /// </param>
    /// <param name="eventType">
    /// Optional logical event-type name stamped onto the message's <c>EventType</c> application
    /// property, so a consumer can dispatch by an explicit type rather than sniffing the payload shape.
    /// The outbox drain supplies the stored <c>OutboxMessage.EventType</c>.
    /// </param>
    Task PublishAsync<T>(T message, string topic, string? correlationId = null, string? traceParent = null, string? eventType = null) where T : class;
}
