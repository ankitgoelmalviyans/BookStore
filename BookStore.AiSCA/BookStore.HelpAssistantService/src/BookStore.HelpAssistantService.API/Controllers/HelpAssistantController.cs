using BookStore.HelpAssistantService.Core.Abstractions;
using BookStore.HelpAssistantService.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.HelpAssistantService.API.Controllers;

/// <summary>
/// Anonymous by design — this is a public FAQ widget shown to logged-out visitors too, not a
/// gated BookStore feature. It never sees or forwards a BookStore auth token; the Angular widget
/// calls this endpoint directly (see help-assistant.component.ts), and this service is the only
/// thing that holds Foundry credentials.
/// </summary>
[ApiController]
[Route("api/help-assistant")]
[AllowAnonymous]
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

        var conversation = request.Messages
            .Select(m => new ChatTurn(
                string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User,
                m.Content ?? string.Empty))
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
