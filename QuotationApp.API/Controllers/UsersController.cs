using Microsoft.AspNetCore.Mvc;
using QuotationApp.API.Services;

namespace QuotationApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _userService.GetAllAsync();
        var result = users.Select(u => new
        {
            u.Id,
            u.Email,
            u.FirstName,
            u.LastName,
            u.Role,
            u.IsActive,
            u.CreatedAt,
            u.LastLoginAt,
            PasswordHash = "••••••••"
        });
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user == null) return NotFound(new { error = "User not found" });

        return Ok(new
        {
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            PasswordHash = "••••••••"
        });
    }

    public record CreateUserRequest(string Email, string Password, string FirstName, string LastName, string Role);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) ||
            string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName))
            return BadRequest(new { error = "All fields are required" });

        var existing = await _userService.GetByEmailAsync(req.Email);
        if (existing != null) return BadRequest(new { error = "User already exists" });

        var user = await _userService.CreateUserAsync(req.Email, req.Password, req.FirstName, req.LastName, req.Role);
        return Ok(new
        {
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            PasswordHash = "••••••••"
        });
    }

    public record UpdateUserRequest(string FirstName, string LastName, string Email, string Role, bool IsActive, string? Password);

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName) ||
            string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { error = "All fields are required" });

        var user = await _userService.UpdateUserAsync(id, req.FirstName, req.LastName, req.Email, req.Role, req.IsActive, req.Password);
        if (user == null) return NotFound(new { error = "User not found" });

        return Ok(new
        {
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            PasswordHash = "••••••••"
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _userService.DeleteUserAsync(id);
        if (!deleted) return NotFound(new { error = "User not found" });

        return NoContent();
    }
}