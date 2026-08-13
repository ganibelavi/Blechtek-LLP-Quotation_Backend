using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using QuotationApp.API.Services;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IConfiguration _config;

    public AuthController(IUserService userService, IConfiguration config)
    {
        _userService = userService;
        _config = config;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Email and password required" });

        var valid = await _userService.ValidateCredentialsAsync(req.Email, req.Password);
        if (!valid) return Unauthorized(new { error = "Invalid credentials" });

        await _userService.RecordLoginAsync(req.Email, HttpContext.Connection.RemoteIpAddress?.ToString());

        var token = GenerateToken(req.Email);
        return Ok(new { token, email = req.Email });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Email and password required" });

        var existing = await _userService.GetByEmailAsync(req.Email);
        if (existing != null) return BadRequest(new { error = "User already exists" });

        var user = await _userService.CreateUserAsync(req.Email, req.Password);
        return Ok(new { email = user.Email });
    }

    private string GenerateToken(string email)
    {
        var jwtSection = _config.GetSection("Jwt");
        var key = jwtSection.GetValue<string>("Key") ?? throw new InvalidOperationException("Jwt:Key missing");
        var issuer = jwtSection.GetValue<string>("Issuer");
        var audience = jwtSection.GetValue<string>("Audience");
        var expires = DateTime.UtcNow.AddMinutes(jwtSection.GetValue<int>("ExpireMinutes", 120));

        var claims = new[] { new Claim(ClaimTypes.Name, email), new Claim(ClaimTypes.Email, email) };
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(issuer, audience, claims, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record LoginRequest(string Email, string Password);
