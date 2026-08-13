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

    public async Task<UserEntity> CreateUserAsync(string email, string password)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var user = new UserEntity { Email = email, PasswordHash = hash };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task RecordLoginAsync(string email, string? remoteAddress = null)
    {
        var entry = new LoginHistoryEntity { Email = email, LoggedAt = DateTime.UtcNow, RemoteAddress = remoteAddress };
        _db.LoginHistory.Add(entry);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ValidateCredentialsAsync(string email, string password)
    {
        var user = await GetByEmailAsync(email);
        if (user == null) return false;
        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
    }
}
