using BookStore.HelpAssistantService.Core.Abstractions;
using BookStore.HelpAssistantService.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookStore.HelpAssistantService.API.Controllers;

/// <summary>
/// Anonymous by design — this is a public FAQ widget shown to logged-out visitors too, not a
/// gated BookStore feature. It never sees or forwards a BookStore auth token; the Angular widget
/// calls this endpoint directly (see help-assistant.component.ts), and this service is the only
/// thing that holds Foundry credentials. Rate-limited (see Program.cs's "help-assistant" policy)
/// since anonymous + no auth means IP is the only abuse signal available.
/// </summary>
[ApiController]
[Route("api/help-assistant")]
[AllowAnonymous]
[EnableRateLimiting("help-assistant")]
public class HelpAssistantController : ControllerBase
{
    private readonly IHelpAssistantService _helpAssistantService;

    public HelpAssistantController(IHelpAssistantService helpAssistantService)
    {
        _helpAssistantService = helpAssistantService;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken cancellationToken)
    {
        if (request?.Messages is null || request.Messages.Count == 0)
        {
            return BadRequest("At least one message is required.");
        }

        foreach (var message in request.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Content))
            {
                return BadRequest("Every message must have non-empty content.");
            }

            if (!string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest($"Unsupported role '{message.Role}' — only 'user' and 'assistant' are allowed.");
            }
        }

        var conversation = request.Messages
            .Select(m => new ChatTurn(
                string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User,
                m.Content))
            .ToList();

        var answer = await _helpAssistantService.AskAsync(conversation, cancellationToken);
        return Ok(new AskResponse { Answer = answer });
    }
}

public class AskRequest
{
    public List<AskMessage> Messages { get; set; } = new();
}

public class AskMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public class AskResponse
{
    public string Answer { get; set; } = string.Empty;
}
