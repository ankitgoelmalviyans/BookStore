namespace BookStore.HelpAssistantService.Core.Entities;

public enum ChatRole
{
    User,
    Assistant
}

public record ChatTurn(ChatRole Role, string Content);
