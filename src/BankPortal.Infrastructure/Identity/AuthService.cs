using System.Security.Cryptography;
using BankPortal.Application.Auth;
using BankPortal.Domain.Entities;
using BankPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BankPortal.Infrastructure.Identity;

public class AuthService(
    BankDbContext db,
    UserManager<ApplicationUser> users,
    TokenService tokens,
    IOptions<JwtOptions> jwtOptions) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = new ApplicationUser { UserName = email, Email = email };

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            throw new AuthException(string.Join(" ", created.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, Roles.Customer);

        var customer = new Customer(user.Id, request.FullName.Trim(), email);
        db.Customers.Add(customer);
        db.Accounts.Add(new Account(customer.Id, NextAccountNumber()));
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return await IssueTokensAsync(user, replacing: null, ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var invalid = new AuthException("Invalid email or password.", 401);

        var user = await users.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null) throw invalid;

        if (await users.IsLockedOutAsync(user))
            throw new AuthException("Account temporarily locked. Try again later.", 423);

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw invalid;
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueTokensAsync(user, replacing: null, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var invalid = new AuthException("Invalid refresh token.", 401);
        var hash = TokenService.Hash(refreshToken);
        var now = DateTime.UtcNow;

        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) throw invalid;

        // A rotated token being replayed means it was probably stolen: kill every session for this user.
        if (stored.RevokedAt is not null)
        {
            var active = await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var t in active) t.Revoke(now);
            await db.SaveChangesAsync(ct);
            throw invalid;
        }

        if (stored.ExpiresAt <= now) throw invalid;

        var user = await users.FindByIdAsync(stored.UserId);
        if (user is null || await users.IsLockedOutAsync(user)) throw invalid;

        return await IssueTokensAsync(user, replacing: stored, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return;               // logout is idempotent
        stored.Revoke(DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResult> IssueTokensAsync(ApplicationUser user, RefreshToken? replacing, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        var (access, expires) = tokens.CreateAccessToken(user, roles);

        var raw = TokenService.GenerateRefreshToken();
        var hash = TokenService.Hash(raw);
        db.RefreshTokens.Add(new RefreshToken(user.Id, hash, DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays)));
        replacing?.Revoke(DateTime.UtcNow, hash);

        await db.SaveChangesAsync(ct);
        return new AuthResult(access, expires, raw);
    }

    private static string NextAccountNumber()
    {
        // 12 digits, e.g. "10" + 10 random digits. The unique index catches the rare collision.
        var digits = string.Concat(Enumerable.Range(0, 10).Select(_ => RandomNumberGenerator.GetInt32(10)));
        return "10" + digits;
    }
}