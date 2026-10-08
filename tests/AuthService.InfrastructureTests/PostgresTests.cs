using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using AuthService.Domain.ServiceKeys;
using AuthService.Domain.Sessions;
using AuthService.Infrastructure.Postgres;
using AuthService.InfrastructureTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AuthService.InfrastructureTests;

/// <summary>Миграции базы сервиса на настоящем Postgres.</summary>
[Collection(InfrastructureCollection.NAME)]
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class MigrationsTests(InfrastructureFixture fixture)
{
    [Fact]
    public async Task AllMigrationsApplied_ModelMatchesSnapshot()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AuthDbContext db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        // Модель и последний snapshot совпадают: изменение сущности без миграции здесь и упадёт.
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Tables_InAuthSchema()
    {
        await using NpgsqlConnection connection = await fixture.OpenDatabaseAsync();
        await using var command = new NpgsqlCommand(
            "select table_name from information_schema.tables where table_schema = 'auth' order by table_name", connection);
        var tables = new List<string>();
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Equal(
            ["__ef_migrations_history", "audit_entries", "data_protection_keys", "login_devices", "service_keys"],
            tables);
    }

    [Fact]
    public async Task Migrate_Twice_NoChanges()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        AuthDbContext db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}

/// <summary>Репозитории базы: каждая проверка — на пустых таблицах (Respawn).</summary>
public abstract class PostgresTest(InfrastructureFixture fixture) : IAsyncLifetime
{
    protected static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    protected InfrastructureFixture Fixture { get; } = fixture;

    public Task InitializeAsync() => Fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Свой scope — свой DbContext, как у отдельного запроса к сервису.</summary>
    protected async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using AsyncServiceScope scope = Fixture.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    protected async Task InScopeAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using AsyncServiceScope scope = Fixture.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}

[Collection(InfrastructureCollection.NAME)]
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class AuditLogTests(InfrastructureFixture fixture) : PostgresTest(fixture)
{
    private static readonly Guid s_user = Guid.CreateVersion7();
    private static readonly Guid s_admin = Guid.CreateVersion7();
    private static readonly Guid s_otherAdmin = Guid.CreateVersion7();

    [Fact]
    public async Task Record_Search_RoundTripsAllFields()
    {
        AuditEntry entry = AuditEntry.Create(
            Now, "user.blocked", "user", s_user, "79990000001", """{"reason":"spam"}""", s_admin, "admin", "admin", "trace-1", "sandbox-api");
        await InScopeAsync<IAuditLog>(log => log.RecordAsync(entry, CancellationToken.None));

        AuditPage page = await SearchAsync(new AuditQuery(null, null, null, null, null, 1, 10));

        AuditEntry found = Assert.Single(page.Items);
        Assert.False(page.HasMore);
        Assert.Equal(entry.Id, found.Id);
        Assert.Equal(Now, found.OccurredAt);
        Assert.Equal("user.blocked", found.Action);
        Assert.Equal("user", found.ObjectType);
        Assert.Equal(s_user, found.ObjectId);
        Assert.Equal("79990000001", found.ObjectName);
        Assert.Equal("""{"reason": "spam"}""", found.Changes); // jsonb: Postgres нормализует пробелы
        Assert.Equal(s_admin, found.ActorId);
        Assert.Equal("admin", found.ActorName);
        Assert.Equal("admin", found.ActorRole);
        Assert.Equal("trace-1", found.CorrelationId);
        Assert.Equal("sandbox-api", found.App);
    }

    [Fact]
    public async Task Search_Filters()
    {
        await RecordAsync(
            Entry(Now.AddMinutes(-30), "user.blocked", s_user, s_admin, app: "sandbox-api"),
            Entry(Now.AddMinutes(-20), "user.unblocked", s_user, s_otherAdmin, app: "manager-1c"),
            Entry(Now.AddMinutes(-10), "user.blocked", Guid.CreateVersion7(), s_admin));

        Assert.Equal(2, (await SearchAsync(Query(objectId: s_user))).Items.Count);
        Assert.Equal(2, (await SearchAsync(Query(actorId: s_admin))).Items.Count);
        Assert.Equal(2, (await SearchAsync(Query(action: "user.blocked"))).Items.Count);
        Assert.Equal("user.unblocked", Assert.Single((await SearchAsync(Query(app: "manager-1c"))).Items).Action);
        Assert.Equal(2, (await SearchAsync(Query(from: Now.AddMinutes(-20)))).Items.Count);
        Assert.Equal(2, (await SearchAsync(Query(to: Now.AddMinutes(-20)))).Items.Count);
        Assert.Single((await SearchAsync(Query(objectId: s_user, actorId: s_admin, action: "user.blocked"))).Items);
    }

    [Fact]
    public async Task Search_NewestFirst_Paged()
    {
        await RecordAsync([.. Enumerable.Range(0, 5).Select(minute => Entry(Now.AddMinutes(minute), $"action.{minute}", s_user, s_admin))]);

        AuditPage first = await SearchAsync(Query(page: 1, pageSize: 2));
        AuditPage last = await SearchAsync(Query(page: 3, pageSize: 2));

        Assert.Equal(["action.4", "action.3"], first.Items.Select(entry => entry.Action));
        Assert.True(first.HasMore);
        Assert.Equal(["action.0"], last.Items.Select(entry => entry.Action));
        Assert.False(last.HasMore);
    }

    private static AuditEntry Entry(DateTimeOffset at, string action, Guid objectId, Guid actorId, string? app = null) =>
        AuditEntry.Create(at, action, "user", objectId, null, "{}", actorId, null, null, null, app);

    private static AuditQuery Query(
        Guid? objectId = null,
        Guid? actorId = null,
        string? action = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50,
        string? app = null) =>
        new(objectId, actorId, action, from, to, page, pageSize, app);

    private async Task RecordAsync(params AuditEntry[] entries)
    {
        foreach (AuditEntry entry in entries)
        {
            await InScopeAsync<IAuditLog>(log => log.RecordAsync(entry, CancellationToken.None));
        }
    }

    private Task<AuditPage> SearchAsync(AuditQuery query) =>
        InScopeAsync<IAuditLog, AuditPage>(log => log.SearchAsync(query, CancellationToken.None));
}

[Collection(InfrastructureCollection.NAME)]
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class ServiceKeyStoreTests(InfrastructureFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task Add_Find_List_Rotate_Remove()
    {
        Guid account = Guid.CreateVersion7();
        Guid issuer = Guid.CreateVersion7();
        await InScopeAsync<IServiceKeyStore>(store => store.AddAsync(
            ServiceKey.Issue("orders", "Заказы", account, "protected-1", Now, issuer), CancellationToken.None));
        await InScopeAsync<IServiceKeyStore>(store => store.AddAsync(
            ServiceKey.Issue("billing", "Оплата", Guid.CreateVersion7(), "protected-2", Now, null), CancellationToken.None));

        IReadOnlyList<ServiceKey> keys = await InScopeAsync<IServiceKeyStore, IReadOnlyList<ServiceKey>>(
            store => store.ListAsync(CancellationToken.None));
        Assert.Equal(["billing", "orders"], keys.Select(key => key.Code));

        await InScopeAsync<IServiceKeyStore>(async store =>
        {
            ServiceKey key = (await store.FindAsync("orders", CancellationToken.None))!;
            Assert.Equal("service-orders", key.ClientId);
            Assert.Equal("Заказы", key.Name);
            Assert.Equal(account, key.ServiceAccountId);
            Assert.Equal(issuer, key.IssuedBy);
            Assert.Null(key.RotatedAt);

            key.Rotated("protected-3", Now.AddDays(1));
            await store.SaveAsync(key, CancellationToken.None);
        });

        await InScopeAsync<IServiceKeyStore>(async store =>
        {
            ServiceKey key = (await store.FindAsync("orders", CancellationToken.None))!;
            Assert.Equal("protected-3", key.ProtectedRegistrationToken);
            Assert.Equal(Now.AddDays(1), key.RotatedAt);

            await store.RemoveAsync(key, CancellationToken.None);
        });

        Assert.Null(await InScopeAsync<IServiceKeyStore, ServiceKey?>(store => store.FindAsync("orders", CancellationToken.None)));
    }

    [Fact]
    public async Task Add_SameCode_Rejected()
    {
        await InScopeAsync<IServiceKeyStore>(store => store.AddAsync(
            ServiceKey.Issue("orders", "Заказы", Guid.CreateVersion7(), "protected-1", Now, null), CancellationToken.None));

        await Assert.ThrowsAsync<DbUpdateException>(() => InScopeAsync<IServiceKeyStore>(store => store.AddAsync(
            ServiceKey.Issue("orders", "Заказы 2", Guid.CreateVersion7(), "protected-2", Now, null), CancellationToken.None)));
    }
}

[Collection(InfrastructureCollection.NAME)]
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class LoginDeviceStoreTests(InfrastructureFixture fixture) : PostgresTest(fixture)
{
    private static readonly Guid s_user = Guid.CreateVersion7();

    [Fact]
    public async Task Record_SameSessionTwice_KeepsFirst()
    {
        await RecordAsync(LoginDevice.Record("session-1", s_user, "Firefox", Now));
        await RecordAsync(LoginDevice.Record("session-1", s_user, "Chrome", Now.AddMinutes(5)));
        await RecordAsync(LoginDevice.Record("session-2", s_user, null, Now));

        IReadOnlyDictionary<string, string?> agents = await UserAgentsAsync(s_user, "session-1", "session-2", "session-3");

        Assert.Equal(2, agents.Count);
        Assert.Equal("Firefox", agents["session-1"]);
        Assert.Null(agents["session-2"]);
    }

    [Fact]
    public async Task GetUserAgents_OnlyThisUser()
    {
        await RecordAsync(LoginDevice.Record("session-1", Guid.CreateVersion7(), "Firefox", Now));

        Assert.Empty(await UserAgentsAsync(s_user, "session-1"));
    }

    [Fact]
    public async Task Record_RemovesExpired()
    {
        await RecordAsync(LoginDevice.Record("old", s_user, "Firefox", Now - LoginDevice.Retention - TimeSpan.FromMinutes(1)));
        await RecordAsync(LoginDevice.Record("new", s_user, "Chrome", Now));

        IReadOnlyDictionary<string, string?> agents = await UserAgentsAsync(s_user, "old", "new");

        Assert.Equal(["new"], agents.Keys);
    }

    private Task RecordAsync(LoginDevice device) =>
        InScopeAsync<ILoginDeviceStore>(store => store.RecordAsync(device, CancellationToken.None));

    private Task<IReadOnlyDictionary<string, string?>> UserAgentsAsync(Guid user, params string[] sessions) =>
        InScopeAsync<ILoginDeviceStore, IReadOnlyDictionary<string, string?>>(
            store => store.GetUserAgentsAsync(user, sessions, CancellationToken.None));
}
