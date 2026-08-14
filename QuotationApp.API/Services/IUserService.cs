using QuotationApp.API.Data;

namespace QuotationApp.API.Services;

public interface IUserService
{
    Task<UserEntity?> GetByEmailAsync(string email);
    Task<UserEntity?> GetByIdAsync(int id);
    Task<List<UserEntity>> GetAllAsync();
    Task<UserEntity> CreateUserAsync(string email, string password, string firstName, string lastName, string role);
    Task<UserEntity?> UpdateUserAsync(int id, string firstName, string lastName, string email, string role, bool isActive, string? password = null);
    Task<bool> DeleteUserAsync(int id);
    Task RecordLoginAsync(string email, string? remoteAddress = null);
    Task<bool> ValidateCredentialsAsync(string email, string password);
}
