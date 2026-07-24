using BookStore.HelpAssistantService.Core.Abstractions;
using BookStore.HelpAssistantService.Core.Entities;
using Microsoft.Extensions.Logging;

namespace BookStore.HelpAssistantService.Application.Services;

/// <summary>
/// Thin orchestration in front of the Foundry agent client: bounds conversation size before it
/// ever reaches Foundry, since this endpoint is anonymous (public help widget, no BookStore login
/// required) and unbounded input directly drives Foundry token cost.
/// </summary>
public class HelpAssistantChatService : IHelpAssistantService
{
    private const int MaxTurns = 20;
    private const int MaxMessageLength = 2000;

    private readonly IHelpAssistantAgentClient _agentClient;
    private readonly ILogger<HelpAssistantChatService> _logger;

    public HelpAssistantChatService(IHelpAssistantAgentClient agentClient, ILogger<HelpAssistantChatService> logger)
    {
        _agentClient = agentClient;
        _logger = logger;
    }

    public async Task<string> AskAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken)
    {
        if (conversation is null || conversation.Count == 0)
        {
            throw new ArgumentException("Conversation must contain at least one message.", nameof(conversation));
        }

        var trimmed = conversation
            .TakeLast(MaxTurns)
            .Select(turn => turn with { Content = Truncate(turn.Content, MaxMessageLength) })
            .ToList();

        _logger.LogInformation("Forwarding conversation with {TurnCount} turns to the Foundry agent", trimmed.Count);

        return await _agentClient.AskAsync(trimmed, cancellationToken);
    }

    private static string Truncate(string content, int maxLength) =>
        content.Length <= maxLength ? content : content[..maxLength];
}
