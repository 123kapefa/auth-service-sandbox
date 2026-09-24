using AuthService.Domain.Identity;
using Xunit;

namespace AuthService.UnitTests;

public sealed class AccountTests
{
    [Fact]
    public void ConstructorAssignsUuidV7AndAuditFields()
    {
        var account = new Account("alice@example.test", "alice", "Alice");

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("alice@example.test", account.Email);
        Assert.Equal("alice", account.UserName);
        Assert.Equal("Alice", account.DisplayName);
        Assert.Equal(account.CreatedAt, account.UpdatedAt);
        Assert.False(string.IsNullOrWhiteSpace(account.SecurityStamp));
    }
}
