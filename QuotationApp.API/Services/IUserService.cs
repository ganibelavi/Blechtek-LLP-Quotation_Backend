using QuotationApp.API.Data;

namespace QuotationApp.API.Services;

public interface IUserService
{
    Task<UserEntity?> GetByEmailAsync(string email);
    Task<UserEntity> CreateUserAsync(string email, string password);
    Task RecordLoginAsync(string email, string? remoteAddress = null);
    Task<bool> ValidateCredentialsAsync(string email, string password);
}
