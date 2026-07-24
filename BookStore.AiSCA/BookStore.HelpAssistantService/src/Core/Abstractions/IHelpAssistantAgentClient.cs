using BookStore.HelpAssistantService.Core.Entities;

namespace BookStore.HelpAssistantService.Core.Abstractions;

/// <summary>
/// Invokes the published Azure AI Foundry Agent Application backing the Help Assistant.
/// Implementations own their own Entra ID authentication — callers never see a token or key.
/// </summary>
public interface IHelpAssistantAgentClient
{
    Task<string> AskAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken);
}
