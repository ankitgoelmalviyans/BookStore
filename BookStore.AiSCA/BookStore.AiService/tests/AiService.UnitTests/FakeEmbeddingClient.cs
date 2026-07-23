using BookStore.AiService.Core.Abstractions;

namespace BookStore.AiService.UnitTests;

/// <summary>Records every text it was asked to embed and returns a fixed, tiny vector — no need for real embedding math in a unit test.</summary>
internal sealed class FakeEmbeddingClient : IEmbeddingClient
{
    public List<string> EmbeddedTexts { get; } = new();

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        EmbeddedTexts.Add(text);
        return Task.FromResult(new float[] { 1f, 0f, 0f });
    }
}
