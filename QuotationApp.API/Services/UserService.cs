using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Data;

namespace QuotationApp.API.Services;

public class UserService : IUserService
{
    private readonly QuotationDbContext _db;

    public UserService(QuotationDbContext db)
    {
        _db = db;
    }

    public async Task<UserEntity?> GetByEmailAsync(string email)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<UserEntity?> GetByIdAsync(int id)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<List<UserEntity>> GetAllAsync()
    {
        return await _db.Users.OrderBy(u => u.Email).ToListAsync();
    }

    public async Task<UserEntity> CreateUserAsync(string email, string password, string firstName, string lastName, string role)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var user = new UserEntity
        {
            Email = email,
            PasswordHash = hash,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<UserEntity?> UpdateUserAsync(int id, string firstName, string lastName, string email, string role, bool isActive, string? password = null)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;

        user.FirstName = firstName;
        user.LastName = lastName;
        user.Email = email;
        user.Role = role;
        user.IsActive = isActive;

        if (!string.IsNullOrWhiteSpace(password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        }

        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return false;

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task RecordLoginAsync(string email, string? remoteAddress = null)
    {
        var entry = new LoginHistoryEntity { Email = email, LoggedAt = DateTime.UtcNow, RemoteAddress = remoteAddress };
        _db.LoginHistory.Add(entry);
        
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
        }
        
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ValidateCredentialsAsync(string email, string password)
    {
        var user = await GetByEmailAsync(email);
        if (user == null) return false;
        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
    }
}
