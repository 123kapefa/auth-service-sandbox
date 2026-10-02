using System.Text.Json;
using AuthService.LegacyImport;
using Xunit;

namespace AuthService.UnitTests;

// Перенос пользователей боевого сервиса (этап 6): сопоставление полей и разбор ответа partialImport.
public sealed class LegacyImportTests
{
    private const string HASH = "$2a$12$oTSY.ajW6NkI0GFoqNLp9.dcm/Fk.icpb.TFe1fQY20qAwdP72awq";
    private static readonly Guid _id = Guid.Parse("1e9ac700-0000-4000-8000-000000000001");
    private static readonly KeycloakTarget _target = new() { Realm = "sandbox", RolesClientId = "sandbox-api" };
    private static readonly DateTimeOffset _created = new(2025, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Customer_WithPassword_KeepsIdAndBcryptHash()
    {
        MappingResult result = KeycloakUserMapper.Map(Legacy(), _target);

        UserRepresentation user = Assert.IsType<UserRepresentation>(result.User);
        Assert.Equal(_id, user.Id);
        Assert.Equal("79991110001", user.Username);
        Assert.True(user.Enabled);
        Assert.Equal(_created.ToUnixTimeMilliseconds(), user.CreatedTimestamp);
        Assert.Equal(["+79991110001"], user.Attributes["phone"]);
        Assert.Equal(["customer"], user.Attributes["active_mode"]);
        Assert.Equal(["42"], user.Attributes["legacy_login_count"]);
        Assert.Equal(["2026-09-30T10:15:00.0000000+00:00"], user.Attributes["legacy_last_login_at"]);
        Assert.Equal(["default-roles-sandbox"], user.RealmRoles);
        Assert.Null(user.ClientRoles);
        Assert.Empty(result.Notes);

        CredentialRepresentation credential = Assert.Single(user.Credentials);
        Assert.Equal("password", credential.Type);
        Assert.Equal(HASH, JsonDocument.Parse(credential.SecretData).RootElement.GetProperty("value").GetString());
        JsonElement data = JsonDocument.Parse(credential.CredentialData).RootElement;
        Assert.Equal("bcrypt", data.GetProperty("algorithm").GetString());
        Assert.Equal(12, data.GetProperty("hashIterations").GetInt32());
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("admin")]
    public void Staff_GetAppRole_AndMode(string role)
    {
        UserRepresentation user = KeycloakUserMapper.Map(Legacy() with { Role = role }, _target).User!;

        Assert.Equal([role], Assert.IsType<Dictionary<string, string[]>>(user.ClientRoles)["sandbox-api"]);
        Assert.Equal([role], user.Attributes["active_mode"]);
    }

    [Fact]
    public void UnknownRole_BecomesCustomer_WithLegacyRoleAttribute()
    {
        MappingResult result = KeycloakUserMapper.Map(Legacy() with { Role = "courier" }, _target);

        Assert.Null(result.User!.ClientRoles);
        Assert.Equal(["customer"], result.User.Attributes["active_mode"]);
        Assert.Equal(["courier"], result.User.Attributes["legacy_role"]);
        Assert.Contains(result.Notes, note => note.Contains("courier", StringComparison.Ordinal));
    }

    [Fact]
    public void Restricted_IsDisabled_WithReasonAndTime()
    {
        UserRepresentation user = KeycloakUserMapper.Map(
            Legacy() with { IsRestricted = true, RestrictionReason = "Мошенничество", RestrictedAtUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero) },
            _target).User!;

        Assert.False(user.Enabled);
        Assert.Equal(["Мошенничество"], user.Attributes["restriction_reason"]);
        Assert.Equal(["2026-08-15T12:00:00.0000000+00:00"], user.Attributes["restricted_at"]);
    }

    [Fact]
    public void WithoutPassword_NoCredentials_LogsInByCode()
    {
        MappingResult result = KeycloakUserMapper.Map(Legacy() with { PasswordHash = null }, _target);

        Assert.Empty(result.User!.Credentials);
        Assert.Contains("без пароля — вход по коду", result.Notes);
    }

    [Theory]
    [InlineData("plain-text")]
    [InlineData("$2a$12$short")]
    [InlineData("AQAAAAIAAYagAAAAEJ…")] // хэш ASP.NET Identity — не BCrypt
    public void NotBcryptHash_IsNotImported(string hash)
    {
        MappingResult result = KeycloakUserMapper.Map(Legacy() with { PasswordHash = hash }, _target);

        Assert.Empty(result.User!.Credentials);
        Assert.Contains(result.Notes, note => note.Contains("не в формате BCrypt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("+7 999 111")]
    [InlineData("user@mail.ru")]
    public void NotPhone_IsRejected(string phone)
    {
        MappingResult result = KeycloakUserMapper.Map(Legacy() with { Phone = phone }, _target);

        Assert.Null(result.User);
        Assert.NotNull(result.RejectReason);
    }

    [Fact]
    public void PartialImportResults_AreClassified()
    {
        UserRepresentation added = User("79991110001", 1);
        UserRepresentation again = User("79991110002", 2);
        UserRepresentation conflict = User("79990000001", 8);
        UserRepresentation missing = User("79991110003", 3);
        PartialImportResponse response = new(1, 2,
        [
            new("ADDED", "USER", "79991110001", added.Id.ToString()),
            new("SKIPPED", "USER", "79991110002", again.Id.ToString()),
            new("SKIPPED", "USER", "79990000001", "681d5174-82b7-4a88-bdb7-4c84fb902b2f"),
        ]);

        IReadOnlyList<ImportOutcome> outcomes = KeycloakImportClient.Classify([added, again, conflict, missing], response);

        Assert.Equal(
            [ImportStatus.Added, ImportStatus.AlreadyImported, ImportStatus.Conflict, ImportStatus.Failed],
            outcomes.Select(outcome => outcome.Status));
        Assert.Contains("681d5174-82b7-4a88-bdb7-4c84fb902b2f", outcomes[2].Detail, StringComparison.Ordinal);
    }

    private static LegacyUser Legacy() => new(
        _id, "+79991110001", "customer", false, null, null,
        new DateTimeOffset(2026, 9, 30, 10, 15, 0, TimeSpan.Zero), 42, HASH, _created, _created);

    private static UserRepresentation User(string username, int n) =>
        KeycloakUserMapper.Map(Legacy() with { Id = Guid.Parse($"1e9ac700-0000-4000-8000-00000000000{n}"), Phone = "+" + username }, _target).User!;
}
