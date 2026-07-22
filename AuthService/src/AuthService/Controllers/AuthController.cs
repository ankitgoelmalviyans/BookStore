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

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            await _db.SaveChangesAsync();
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

        if (request.Password.Length < 8)
        {
            return BadRequest(new { message = "Password must be at least 8 characters." });
        }

        if (request.Username.Length > 200)
        {
            return BadRequest(new { message = "Username must be at most 200 characters." });
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

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The AnyAsync check above and this insert aren't atomic — a concurrent registration
            // for the same username can slip in between them. The unique index on Username is the
            // real guard; this just turns that race's failure mode into the same Conflict response
            // instead of an unhandled 500.
            return Conflict(new { message = "Username is already taken." });
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = "Registration submitted. An administrator must activate your account before you can log in."
        });
    }

    // A self-service /reset-password endpoint was removed here: taking just a Username with no
    // current password, email token, or other proof of ownership let anyone force any account's
    // IsActive to false on demand (repeatable lockout), and worse, let an attacker pre-set the
    // NewPassword an admin would later unknowingly reactivate. Closing this properly needs a real
    // token/email-verification flow (deferred — no email infra exists yet). Until then, password
    // resets are a manual database operation, the same as account activation.
}
