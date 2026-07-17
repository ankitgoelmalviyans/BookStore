namespace BookStore.PaymentService.Core.Messaging;

public interface IMessagePublisher
{
    /// <param name="correlationId">Explicit CorrelationId to stamp (supplied by the outbox drain,
    /// which runs outside any HTTP request). When null, falls back to the current request, then a new GUID.</param>
    /// <param name="traceParent">Optional W3C traceparent so the publish span joins the originating trace.</param>
    Task PublishAsync<T>(T message, string topic, string? correlationId = null, string? traceParent = null, string? eventType = null) where T : class;
}
