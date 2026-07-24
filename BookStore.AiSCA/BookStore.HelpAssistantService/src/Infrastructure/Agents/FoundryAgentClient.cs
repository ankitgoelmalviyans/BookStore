using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using BookStore.HelpAssistantService.Core.Abstractions;
using BookStore.HelpAssistantService.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookStore.HelpAssistantService.Infrastructure.Agents;

/// <summary>
/// Calls the published Foundry Agent Application's Responses-protocol endpoint. Authenticates
/// with an Entra ID app registration (client-credentials flow) — never an API key — because
/// Agent Applications only accept Entra ID/RBAC callers; see
/// https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/agent-applications. This is the
/// one place in the platform that talks to Azure AI Foundry directly; the Angular SPA never sees
/// the endpoint, the app registration, or any credential — it only calls this service's own
/// anonymous /api/help-assistant/ask endpoint.
/// </summary>
public class FoundryAgentClient : IHelpAssistantAgentClient
{
    private static readonly string[] Scopes = { "https://ai.azure.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly FoundryAgentOptions _options;
    private readonly ILogger<FoundryAgentClient> _logger;
    private readonly ClientSecretCredential _credential;

    public FoundryAgentClient(HttpClient httpClient, IOptions<FoundryAgentOptions> options, ILogger<FoundryAgentClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credential = new ClientSecretCredential(_options.TenantId, _options.ClientId, _options.ClientSecret);
    }

    public async Task<string> AskAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);

        var url =
            $"https://{_options.AccountName}.services.ai.azure.com/api/projects/{_options.ProjectName}" +
            $"/applications/{_options.ApplicationName}/protocols/openai/responses?api-version={_options.ApiVersion}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = JsonContent.Create(new ResponsesRequest
        {
            Input = conversation.Select(turn => new ResponsesMessage
            {
                Role = turn.Role == ChatRole.User ? "user" : "assistant",
                Content = turn.Content
            }).ToArray()
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Foundry agent call failed with {StatusCode}: {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Foundry agent request failed with status {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadFromJsonAsync<ResponsesResponse>(cancellationToken: cancellationToken);
        return ExtractAnswer(payload) ?? "Sorry, I could not get an answer. Please try again.";
    }

    private static string? ExtractAnswer(ResponsesResponse? response)
    {
        if (response is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(response.OutputText))
        {
            return response.OutputText;
        }

        // Fall back to walking the structured output array — output_text is a convenience field
        // that isn't guaranteed present on every Responses API version.
        return response.Output?
            .SelectMany(item => item.Content ?? Array.Empty<ResponsesContent>())
            .Select(content => content.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private class ResponsesRequest
    {
        [JsonPropertyName("input")]
        public ResponsesMessage[] Input { get; set; } = Array.Empty<ResponsesMessage>();
    }

    private class ResponsesMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class ResponsesResponse
    {
        [JsonPropertyName("output_text")]
        public string? OutputText { get; set; }

        [JsonPropertyName("output")]
        public ResponsesOutputItem[]? Output { get; set; }
    }

    private class ResponsesOutputItem
    {
        [JsonPropertyName("content")]
        public ResponsesContent[]? Content { get; set; }
    }

    private class ResponsesContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
