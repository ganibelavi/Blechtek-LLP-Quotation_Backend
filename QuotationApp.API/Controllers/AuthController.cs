using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using QuotationApp.API.Services;
using System.Security.Cryptography;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IConfiguration _config;
    private readonly IEmailService _emailService;
    private readonly PasswordResetService _passwordResetService;

    public AuthController(IUserService userService, IConfiguration config, IEmailService emailService, PasswordResetService passwordResetService)
    {
        _userService = userService;
        _config = config;
        _emailService = emailService;
        _passwordResetService = passwordResetService;
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

        var user = await _userService.CreateUserAsync(req.Email, req.Password, "", "", "User");
        return Ok(new { email = user.Email });
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] EmailRequest req)
    {
        var email = req.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required" });

        var user = await _userService.GetByEmailAsync(email);
        if (user == null || !user.IsActive)
            return NotFound(new { error = "No active account was found for this email address." });

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var token = _passwordResetService.CreateRequest(email, otp);
        await _emailService.SendPasswordResetOtpAsync(email, otp);
        return Ok(new { verificationToken = token });
    }

    [HttpPost("verify-otp")]
    public IActionResult VerifyOtp([FromBody] VerifyOtpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Otp) || string.IsNullOrWhiteSpace(req.VerificationToken))
            return BadRequest(new { error = "Email, OTP, and verification token are required" });

        return _passwordResetService.VerifyOtp(req.Email.Trim(), req.Otp.Trim(), req.VerificationToken)
            ? Ok(new { message = "OTP verified" })
            : BadRequest(new { error = "Invalid or expired OTP" });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Otp) || string.IsNullOrWhiteSpace(req.VerificationToken))
            return BadRequest(new { error = "Email, OTP, and verification token are required" });

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6 ||
            !req.NewPassword.Any(char.IsUpper) || !req.NewPassword.Any(char.IsDigit))
            return BadRequest(new { error = "Password must be at least 6 characters and include one number and one capital letter." });

        var email = req.Email.Trim();
        if (!_passwordResetService.IsVerified(email, req.Otp.Trim(), req.VerificationToken))
            return BadRequest(new { error = "Please verify a valid OTP before resetting the password." });

        if (!await _userService.UpdatePasswordAsync(email, req.NewPassword))
            return NotFound(new { error = "No active account was found for this email address." });

        _passwordResetService.Consume(email);
        return Ok(new { message = "Password reset successfully" });
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
public record EmailRequest(string Email);
public record VerifyOtpRequest(string Email, string Otp, string VerificationToken);
public record ResetPasswordRequest(string Email, string Otp, string NewPassword, string VerificationToken);
