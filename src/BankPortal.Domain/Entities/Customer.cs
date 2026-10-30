namespace BankPortal.Domain.Entities;

public class Customer
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string UserId { get; private set; } = default!;   // ASP.NET Identity user id (Phase 3)
    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public List<Account> Accounts { get; private set; } = new();

    private Customer() { }   // required by EF Core

    public Customer(string userId, string fullName, string email)
    {
        UserId = userId;
        FullName = fullName;
        Email = email;
    }
}