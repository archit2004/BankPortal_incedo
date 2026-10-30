namespace BankPortal.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string UserId { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;   // SHA-256 hex; the raw token is never stored
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    private RefreshToken() { }

    public RefreshToken(string userId, string tokenHash, DateTime expiresAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public void Revoke(DateTime now, string? replacedByHash = null)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        ReplacedByTokenHash = replacedByHash;
    }
}