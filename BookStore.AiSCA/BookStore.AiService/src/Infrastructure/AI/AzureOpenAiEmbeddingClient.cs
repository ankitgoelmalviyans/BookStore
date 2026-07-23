using Azure;
using Azure.AI.OpenAI;
using BookStore.AiService.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;

namespace BookStore.AiService.Infrastructure.AI;

/// <summary>Real Azure OpenAI embeddings, used only when AzureOpenAI:Key/Endpoint are configured (see StartupExtensions).</summary>
public class AzureOpenAiEmbeddingClient : IEmbeddingClient
{
    private readonly EmbeddingClient _client;

    public AzureOpenAiEmbeddingClient(IConfiguration configuration)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"]!;
        var key = configuration["AzureOpenAI:Key"]!;
        var deployment = configuration["AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        _client = azureClient.GetEmbeddingClient(deployment);
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var response = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        return response.Value.ToFloats().ToArray();
    }
}
