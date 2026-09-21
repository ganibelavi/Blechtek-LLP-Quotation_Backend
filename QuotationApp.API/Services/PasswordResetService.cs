using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace QuotationApp.API.Services;

public sealed class PasswordResetService
{
    private readonly ConcurrentDictionary<string, ResetRequest> _requests = new(StringComparer.OrdinalIgnoreCase);

    public string CreateRequest(string email, string otp)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _requests[email] = new ResetRequest(Hash(otp), token, DateTime.UtcNow.AddMinutes(10), false);
        return token;
    }

    public bool VerifyOtp(string email, string otp, string token)
    {
        if (!_requests.TryGetValue(email, out var request) || request.ExpiresAt < DateTime.UtcNow)
            return false;

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(request.OtpHash),
                Convert.FromHexString(Hash(otp))) || !FixedEquals(request.Token, token))
            return false;

        _requests[email] = request with { Verified = true };
        return true;
    }

    public bool IsVerified(string email, string otp, string token)
    {
        return _requests.TryGetValue(email, out var request)
            && request.Verified
            && request.ExpiresAt >= DateTime.UtcNow
            && FixedEquals(request.Token, token)
            && CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(request.OtpHash),
                Convert.FromHexString(Hash(otp)));
    }

    public void Consume(string email) => _requests.TryRemove(email, out _);

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record ResetRequest(string OtpHash, string Token, DateTime ExpiresAt, bool Verified);
}
