namespace AuthService.Models;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Self-registration and password reset both leave this false — there's no email-verification
    // loop to confirm the requester owns the account, so activation is a manual step (an operator
    // flips this directly in the database) until a real admin flow gets built.
    public bool IsActive { get; set; }
}
