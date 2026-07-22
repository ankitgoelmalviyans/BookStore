namespace AuthService.Models;

public record ResetPasswordRequest(string Username, string NewPassword);
