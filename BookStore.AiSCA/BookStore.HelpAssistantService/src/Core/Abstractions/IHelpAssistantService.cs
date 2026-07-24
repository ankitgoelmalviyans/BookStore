using BookStore.HelpAssistantService.Core.Entities;

namespace BookStore.HelpAssistantService.Core.Abstractions;

public interface IHelpAssistantService
{
    Task<string> AskAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken);
}
