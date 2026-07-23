using Azure;
using Azure.AI.OpenAI;
using BookStore.AiService.Core.Abstractions;
using BookStore.AiService.Core.Entities;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace BookStore.AiService.Infrastructure.AI;

/// <summary>Real Azure OpenAI chat completion, used only when AzureOpenAI:Key/Endpoint are configured (see StartupExtensions). Grounds the answer strictly in the retrieved matches — the "generation" half of RAG.</summary>
public class AzureOpenAiAnswerGenerator : IAnswerGenerator
{
    private readonly ChatClient _client;

    public AzureOpenAiAnswerGenerator(IConfiguration configuration)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"]!;
        var key = configuration["AzureOpenAI:Key"]!;
        var deployment = configuration["AzureOpenAI:ChatDeployment"] ?? "gpt-5-mini";

        var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        _client = azureClient.GetChatClient(deployment);
    }

    public async Task<string> GenerateAsync(string query, IReadOnlyList<BookMatch> matches, CancellationToken cancellationToken = default)
    {
        if (matches.Count == 0)
        {
            return $"No catalog matches found for \"{query}\".";
        }

        var context = string.Join("\n", matches.Select(m => $"- {m.Name}: {m.Description}"));
        var prompt =
            "You are a bookstore assistant. Using ONLY the books listed below, answer the user's " +
            $"request in a couple of sentences. Do not recommend any book not listed.\n\nBooks:\n{context}\n\nUser request: {query}";

        var messages = new List<ChatMessage> { new UserChatMessage(prompt) };
        var completion = await _client.CompleteChatAsync(messages, cancellationToken: cancellationToken);

        return completion.Value.Content.Count > 0 ? completion.Value.Content[0].Text : string.Empty;
    }
}
