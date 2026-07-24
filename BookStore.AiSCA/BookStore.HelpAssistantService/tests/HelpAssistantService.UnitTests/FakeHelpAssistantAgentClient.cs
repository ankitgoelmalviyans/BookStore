using BookStore.HelpAssistantService.Core.Abstractions;
using BookStore.HelpAssistantService.Core.Entities;

namespace BookStore.HelpAssistantService.UnitTests;

public class FakeHelpAssistantAgentClient : IHelpAssistantAgentClient
{
    public IReadOnlyList<ChatTurn>? LastConversationReceived { get; private set; }

    public Task<string> AskAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken)
    {
        LastConversationReceived = conversation;
        return Task.FromResult("fake-answer");
    }
}
