using System.ComponentModel.DataAnnotations;

namespace BankPortal.Application.Auth;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public class RegisterRequest
{
    [Required, MaxLength(200)] public string FullName { get; set; } = "";
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    [Required, MinLength(10), MaxLength(128)] public string Password { get; set; } = "";
}

public class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Password { get; set; } = "";
}

public class RefreshRequest
{
    [Required] public string RefreshToken { get; set; } = "";
}

public record AuthResult(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken);

public class AuthException : Exception
{
    public int StatusCode { get; }
    public AuthException(string message, int statusCode = 400) : base(message) => StatusCode = statusCode;
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct);
    Task LogoutAsync(string refreshToken, CancellationToken ct);
}