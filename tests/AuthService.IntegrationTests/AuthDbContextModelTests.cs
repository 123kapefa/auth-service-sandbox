using AuthService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuthService.IntegrationTests;

public sealed class AuthDbContextModelTests
{
    [Theory]
    [InlineData("AuthService.Domain.Identity.Account", "users")]
    [InlineData("AuthService.Domain.Identity.Role", "roles")]
    [InlineData("AuthService.Infrastructure.Postgres.Identity.OidcApplication", "oidc_applications")]
    [InlineData("AuthService.Infrastructure.Postgres.Identity.OidcToken", "oidc_tokens")]
    public void CriticalEntitiesUseExpectedSnakeCaseTables(string clrType, string table)
    {
        var context = new AuthDbContextFactory().CreateDbContext([]);
        var entity = context.Model.GetEntityTypes().Single(type => type.ClrType.FullName == clrType);

        Assert.Equal(table, entity.GetTableName());
    }
}
