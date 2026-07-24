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
    public async Task AskAsync_forwards_the_conversation_and_returns_the_agent_clients_answer()
    {
        var agentClient = new FakeHelpAssistantAgentClient();
        var sut = new Sut(agentClient, NullLogger<Sut>.Instance);

        var question = new ChatTurn(ChatRole.User, "How do I track my order?");
        var answer = await sut.AskAsync(new[] { question }, CancellationToken.None);

        Assert.Equal("fake-answer", answer);
        Assert.NotNull(agentClient.LastConversationReceived);
        Assert.Single(agentClient.LastConversationReceived!);
        Assert.Equal(question, agentClient.LastConversationReceived![0]);
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
        var received = agentClient.LastConversationReceived!;
        // The most recent 20 of 30 turns (indices 10-29) must survive, in order — not just a
        // count-and-last-element check, which would also pass if the wrong slice were kept.
        Assert.Equal(20, received.Count);
        Assert.Equal(conversation.Skip(10).ToList(), received);
        Assert.Equal("turn 10", received[0].Content);
        Assert.Equal("turn 29", received[^1].Content);
    }

    [Fact]
    public async Task AskAsync_truncates_overly_long_messages()
    {
        var agentClient = new FakeHelpAssistantAgentClient();
        var sut = new Sut(agentClient, NullLogger<Sut>.Instance);

        var longMessage = new string('x', 1996) + "KEEP" + new string('x', 3000);
        await sut.AskAsync(new[] { new ChatTurn(ChatRole.User, longMessage) }, CancellationToken.None);

        var truncated = agentClient.LastConversationReceived![0].Content;
        Assert.Equal(2000, truncated.Length);
        // Confirms truncation kept the leading 2000 chars (not e.g. an off-by-one or a
        // different slice) — "KEEP" sits right at the boundary, so it survives only if the cut
        // point is exactly correct.
        Assert.Equal(longMessage[..2000], truncated);
        Assert.EndsWith("KEEP", truncated);
    }
}
