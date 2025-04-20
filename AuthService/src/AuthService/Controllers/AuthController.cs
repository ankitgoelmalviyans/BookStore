using Microsoft.AspNetCore.Mvc;
using AuthService.Models;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;

    public AuthController(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (request.Username == "admin@gmail.com" && request.Password == "Ankitgoel@123")
        {
            var token = _tokenService.GenerateToken(request.Username);
            return Ok(new { token });
        }

        return Unauthorized();
    }
}
