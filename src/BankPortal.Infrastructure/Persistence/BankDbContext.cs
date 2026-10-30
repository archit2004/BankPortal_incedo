using BankPortal.Domain.Entities;
using BankPortal.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BankPortal.Infrastructure.Persistence;

public class BankDbContext : IdentityDbContext<ApplicationUser>
{
    public BankDbContext(DbContextOptions<BankDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<FixedDeposit> FixedDeposits => Set<FixedDeposit>();
    public DbSet<InterestAccrual> InterestAccruals => Set<InterestAccrual>();
    public DbSet<RateCardEntry> RateCard => Set<RateCardEntry>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            e.HasMany(x => x.Accounts).WithOne(a => a.Customer).HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Customer>()
            .HasOne<ApplicationUser>().WithOne().HasForeignKey<Customer>(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Account>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.AccountNumber).IsUnique();
            e.Property(x => x.AccountNumber).HasMaxLength(20).IsRequired();
            e.Property(x => x.Balance).HasColumnType("decimal(19,4)");
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.ToTable(t => t.HasCheckConstraint("CK_Accounts_Balance_NonNegative", "[Balance] >= 0"));
            e.HasMany(x => x.Transactions).WithOne().HasForeignKey(t => t.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Amount).HasColumnType("decimal(19,4)");
            e.Property(x => x.BalanceAfter).HasColumnType("decimal(19,4)");
            e.Property(x => x.Reference).HasMaxLength(200);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.HasIndex(x => new { x.AccountId, x.IdempotencyKey })
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");
            e.HasIndex(x => new { x.AccountId, x.CreatedAt });
        });

        b.Entity<FixedDeposit>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Principal).HasColumnType("decimal(19,4)");
            e.Property(x => x.AnnualRatePercent).HasColumnType("decimal(7,4)");
            e.Property(x => x.MaturityAmount).HasColumnType("decimal(19,4)");
            e.Property(x => x.PenaltyApplied).HasColumnType("decimal(19,4)");
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Status, x.MaturityDate });
        });

        b.Entity<InterestAccrual>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Amount).HasColumnType("decimal(19,4)");
            e.HasIndex(x => new { x.FixedDepositId, x.AccrualDate }).IsUnique();
            e.HasOne<FixedDeposit>().WithMany().HasForeignKey(x => x.FixedDepositId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RateCardEntry>(e =>
        {
            e.Property(x => x.AnnualRatePercent).HasColumnType("decimal(7,4)");
            e.HasData(
                new { Id = 1, MinTenureMonths = 6,  MaxTenureMonths = 11, AnnualRatePercent = 6.00m, EffectiveFrom = new DateOnly(2026, 1, 1) },
                new { Id = 2, MinTenureMonths = 12, MaxTenureMonths = 23, AnnualRatePercent = 7.00m, EffectiveFrom = new DateOnly(2026, 1, 1) },
                new { Id = 3, MinTenureMonths = 24, MaxTenureMonths = 60, AnnualRatePercent = 7.25m, EffectiveFrom = new DateOnly(2026, 1, 1) });
        });

        b.Entity<AuditLogEntry>(e =>
        {
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.Entity).HasMaxLength(100);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
        });
    }
}