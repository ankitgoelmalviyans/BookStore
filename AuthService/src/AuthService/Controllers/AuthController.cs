using Microsoft.AspNetCore.Mvc;
using AuthService.Models;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    
    public AuthController(ITokenService tokenService, IConfiguration configuration)
    {
        _tokenService = tokenService;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var adminUser = _configuration["Auth:Username"];
        var adminPass = _configuration["Auth:Password"];
    
        if (request.Username == adminUser && request.Password == adminPass)
        {
            var token = _tokenService.GenerateToken(request.Username);
            return Ok(new { token });
        }
    
        return Unauthorized();
    }
}
