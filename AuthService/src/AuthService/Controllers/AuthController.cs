using AuthService.Models;
using AuthService.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly AuthDbContext _db;
    private readonly PasswordHasher<User> _passwordHasher;

    public AuthController(ITokenService tokenService, AuthDbContext db, PasswordHasher<User> passwordHasher)
    {
        _tokenService = tokenService;
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == request.Username);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized();
        }

        if (!user.IsActive)
        {
            return Forbid();
        }

        var token = _tokenService.GenerateToken(user.Username);
        return Ok(new { token });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username and password are required." });
        }

        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
        {
            return Conflict(new { message = "Username is already taken." });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "Registration submitted. An administrator must activate your account before you can log in."
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { message = "Username and new password are required." });
        }

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == request.Username);
        if (user is not null)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            // No email-verification loop to confirm the requester owns the account, so a reset
            // deactivates it — same manual re-activation gate as a fresh registration.
            user.IsActive = false;
            await _db.SaveChangesAsync();
        }

        // Same response whether or not the username existed — don't let this endpoint be used to
        // enumerate valid usernames.
        return Ok(new
        {
            message = "If that account exists, its password has been reset and it now requires administrator reactivation."
        });
    }
}
