using BookStore.HelpAssistantService.Application.Services;
using BookStore.HelpAssistantService.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookStore.HelpAssistantService.UnitTests;

using Sut = HelpAssistantChatService;

public class HelpAssistantChatServiceTests
{
    [Fact]
    public async Task AskAsync_throws_on_empty_conversation()
    {
        var sut = new Sut(new FakeHelpAssistantAgentClient(), NullLogger<Sut>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.AskAsync(Array.Empty<ChatTurn>(), CancellationToken.None));
    }

    [Fact]
    public async Task AskAsync_forwards_the_answer_from_the_agent_client()
    {
        var agentClient = new FakeHelpAssistantAgentClient();
        var sut = new Sut(agentClient, NullLogger<Sut>.Instance);

        var answer = await sut.AskAsync(
            new[] { new ChatTurn(ChatRole.User, "How do I track my order?") },
            CancellationToken.None);

        Assert.Equal("fake-answer", answer);
    }

    [Fact]
    public async Task AskAsync_caps_conversation_to_the_most_recent_turns()
    {
        var agentClient = new FakeHelpAssistantAgentClient();
        var sut = new Sut(agentClient, NullLogger<Sut>.Instance);

        var conversation = Enumerable.Range(0, 30)
            .Select(i => new ChatTurn(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"turn {i}"))
            .ToList();

        await sut.AskAsync(conversation, CancellationToken.None);

        Assert.NotNull(agentClient.LastConversationReceived);
        Assert.Equal(20, agentClient.LastConversationReceived!.Count);
        Assert.Equal("turn 29", agentClient.LastConversationReceived[^1].Content);
    }

    [Fact]
    public async Task AskAsync_truncates_overly_long_messages()
    {
        var agentClient = new FakeHelpAssistantAgentClient();
        var sut = new Sut(agentClient, NullLogger<Sut>.Instance);

        var longMessage = new string('x', 5000);
        await sut.AskAsync(new[] { new ChatTurn(ChatRole.User, longMessage) }, CancellationToken.None);

        Assert.Equal(2000, agentClient.LastConversationReceived![0].Content.Length);
    }
}
