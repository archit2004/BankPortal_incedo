namespace BankPortal.Infrastructure.Identity;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";                // comes from user secrets / env vars
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}