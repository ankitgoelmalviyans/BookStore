namespace BookStore.PaymentService.Core.Payments;

/// <summary>
/// Thrown when a charge fails with a <em>transient</em> gateway fault (see
/// <see cref="ChargeResult.Retryable"/>). It deliberately propagates out of the handler so the Service
/// Bus subscriber abandons the message and the reservation is redelivered for another attempt — rather
/// than recording a terminal PaymentFailed and cancelling the order over a passing outage.
/// </summary>
public class TransientPaymentException : Exception
{
    public TransientPaymentException(string message) : base(message)
    {
    }
}
