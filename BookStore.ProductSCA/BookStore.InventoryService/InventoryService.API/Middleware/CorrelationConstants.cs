namespace BookStore.InventoryService.API.Middleware
{
    /// <summary>
    /// Single source of truth for the CorrelationId key so the middleware that writes it and the
    /// exception handler that reads it can't drift on the string literal.
    /// </summary>
    public static class CorrelationConstants
    {
        /// <summary>Key under which the request's CorrelationId is stored in <c>HttpContext.Items</c>
        /// (also used as the HTTP header name).</summary>
        public const string HttpContextItemKey = "X-Correlation-Id";
    }
}
