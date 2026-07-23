namespace BookStore.AiService.Core.Abstractions;

/// <summary>
/// Turns text into an embedding vector. Real Azure OpenAI implementation when a key is configured,
/// a deterministic fake otherwise — same "builds/demos with no credentials" posture as
/// PaymentService's <c>IPaymentGateway</c>/<c>FakePaymentGateway</c>.
/// </summary>
public interface IEmbeddingClient
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
