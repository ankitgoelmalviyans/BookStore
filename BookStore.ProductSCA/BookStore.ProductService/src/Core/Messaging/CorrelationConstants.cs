namespace BookStore.ProductService.Core.Messaging;

/// <summary>
/// Single source of truth for the CorrelationId key so the middleware that writes it, the controller
/// that reads it, and the producer that stamps it onto Service Bus messages can never silently
/// diverge on the string literal.
/// </summary>
public static class CorrelationConstants
{
    /// <summary>Key under which the request's CorrelationId is stored in <c>HttpContext.Items</c>
    /// (also used as the HTTP header name).</summary>
    public const string HttpContextItemKey = "X-Correlation-Id";
}
