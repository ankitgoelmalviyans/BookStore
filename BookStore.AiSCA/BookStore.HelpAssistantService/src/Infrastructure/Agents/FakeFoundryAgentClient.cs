using BookStore.HelpAssistantService.Core.Abstractions;
using BookStore.HelpAssistantService.Core.Entities;

namespace BookStore.HelpAssistantService.Infrastructure.Agents;

/// <summary>
/// Deterministic stand-in used when no Foundry app registration is configured — same "builds/demos
/// with no credentials" posture as PaymentService's FakePaymentGateway and AiService's
/// FakeAnswerGenerator. Lets the endpoint, the Angular widget, and local dev work end-to-end
/// before the one-time Foundry setup (infra/setup-foundry-agent.sh) has been run.
/// </summary>
public class FakeFoundryAgentClient : IHelpAssistantAgentClient
{
    public Task<string> AskAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken cancellationToken)
    {
        var lastQuestion = conversation.LastOrDefault(turn => turn.Role == ChatRole.User)?.Content ?? "your question";
        return Task.FromResult(
            $"(Foundry agent not configured yet — this is a placeholder response.) You asked: \"{lastQuestion}\"");
    }
}
