using BankPortal.Application.Accounts;
using BankPortal.Application.Auth;
using BankPortal.Infrastructure.Accounts;
using BankPortal.Application.FixedDeposits;
using BankPortal.Infrastructure.FixedDeposits;
using BankPortal.Application.Jobs;
using BankPortal.Infrastructure.Jobs;
using BankPortal.Infrastructure.Identity;
using BankPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;

namespace BankPortal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<BankDbContext>(opt =>
            opt.UseSqlServer(config.GetConnectionString("Default")));

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireNonAlphanumeric = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<BankDbContext>();

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddScoped<TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IFixedDepositService, FixedDepositService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IInterestJobService, InterestJobService>();
        services.Configure<JobOptions>(config.GetSection("Jobs"));
        services.AddHostedService<InterestBackgroundService>();

        return services;
    }
}