using BookStore.AiService.Core.Abstractions;

namespace BookStore.AiService.Infrastructure.AI;

/// <summary>
/// Deterministic, not semantically meaningful — lets the service build/run the ingestion→search
/// pipeline mechanically with no Azure OpenAI credentials configured, same "builds/demos with no
/// credentials" posture as PaymentService's FakePaymentGateway. Same text always produces the same
/// vector; different text produces different (but not meaningfully related) vectors.
/// </summary>
public class FakeEmbeddingClient : IEmbeddingClient
{
    public const int Dimensions = 1536;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var seed = (text ?? string.Empty).GetHashCode();
        var random = new Random(seed);
        var vector = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
        }

        return Task.FromResult(vector);
    }
}
