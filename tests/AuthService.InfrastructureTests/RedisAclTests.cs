using System.Text;
using AuthService.InfrastructureTests.Infrastructure;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth;
using PlatformAuth.Redis;
using PlatformAuth.Revocation;
using StackExchange.Redis;
using Xunit;

namespace AuthService.InfrastructureTests;

/// <summary>
/// ACL Redis из deploy/redis/redis.conf.example на настоящем Redis и настоящем пакете отзыва: сервис входа пишет и читает отметки,
/// сервисы проекта только читают (не могут ни выставить, ни стереть отметку), без имени и пароля Redis молчит.
/// Проверяется сам файл примера, а не его копия: пример, который не работает, хуже, чем никакого.
/// </summary>
[Trait("Category", TestCategories.INFRASTRUCTURE)]
public sealed class RedisAclTests : IAsyncLifetime
{
    private const string SERVICE_PASSWORD = "service-password-for-test";
    private const string READER_PASSWORD = "reader-password-for-test";
    private const string SUBJECT = "6f1c2d3e-0000-4000-8000-0000000000aa";

    private IContainer? _redis;

    public async Task InitializeAsync()
    {
        string config = (await File.ReadAllTextAsync(RepositoryRoot.Path("deploy/redis/redis.conf.example")))
            .Replace("CHANGE_ME_AUTH_SERVICE", SERVICE_PASSWORD, StringComparison.Ordinal)
            .Replace("CHANGE_ME_PROJECT_READONLY", READER_PASSWORD, StringComparison.Ordinal);

        _redis = new ContainerBuilder("redis:7-alpine")
            .WithPortBinding(6379, assignRandomHostPort: true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(config), "/etc/redis/redis.conf")
            .WithCommand("redis-server", "/etc/redis/redis.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
        await _redis.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthService_WritesAndReadsMarks()
    {
        await using ServiceProvider service = Provider("auth-service", SERVICE_PASSWORD);
        ITokenRevocationStore store = service.GetRequiredService<ITokenRevocationStore>();

        await store.RevokeUserAsync(SUBJECT, DateTimeOffset.UtcNow, CancellationToken.None);

        RevocationMarks marks = await store.GetAsync(new TokenKey(SUBJECT, "s-1", "sandbox-bff", "sandbox-api", "customer"), CancellationToken.None);
        Assert.NotEqual(RevocationMarks.None, marks);
    }

    [Fact]
    public async Task ProjectReadonly_ReadsMarksWrittenByAuthService_ThroughThePackage()
    {
        await using ServiceProvider service = Provider("auth-service", SERVICE_PASSWORD);
        await service.GetRequiredService<ITokenRevocationStore>().RevokeUserAsync(SUBJECT, DateTimeOffset.UtcNow, CancellationToken.None);

        await using ServiceProvider reader = Provider("project-readonly", READER_PASSWORD);
        RevocationMarks marks = await reader.GetRequiredService<ITokenRevocationStore>()
            .GetAsync(new TokenKey(SUBJECT, "s-1", "sandbox-bff", "sandbox-api", "customer"), CancellationToken.None);

        Assert.NotEqual(RevocationMarks.None, marks);
    }

    [Fact]
    public async Task ProjectReadonly_CannotWriteOrEraseMarks()
    {
        await using ServiceProvider service = Provider("auth-service", SERVICE_PASSWORD);
        await service.GetRequiredService<ITokenRevocationStore>().RevokeUserAsync(SUBJECT, DateTimeOffset.UtcNow, CancellationToken.None);
        await using ServiceProvider reader = Provider("project-readonly", READER_PASSWORD);
        ITokenRevocationStore store = reader.GetRequiredService<ITokenRevocationStore>();

        // Выставить отметку нельзя (SET), стереть чужую — тоже (DEL, FLUSHALL), прочитать чужие ключи — тоже.
        await Assert.ThrowsAsync<RedisServerException>(() => store.RevokeUserAsync(SUBJECT, DateTimeOffset.UtcNow, CancellationToken.None));
        IDatabase database = reader.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await Assert.ThrowsAsync<RedisServerException>(() => database.KeyDeleteAsync($"auth:revoked:user:{SUBJECT}"));
        await Assert.ThrowsAsync<RedisServerException>(() => database.StringGetAsync("other:key"));

        // FLUSHALL и KEYS клиент без admin-режима не отправляет сам, поэтому — отдельное подключение: отказ должен дать сервер (ACL).
        await using ConnectionMultiplexer admin = await ConnectAdminAsync("project-readonly", READER_PASSWORD);
        await Assert.ThrowsAsync<RedisServerException>(() => admin.GetDatabase().ExecuteAsync("FLUSHALL"));
        await Assert.ThrowsAsync<RedisServerException>(() => admin.GetDatabase().ExecuteAsync("KEYS", "*"));
    }

    [Fact]
    public async Task AuthService_CannotTouchOtherKeysOrAdministerRedis()
    {
        await using ServiceProvider service = Provider("auth-service", SERVICE_PASSWORD);
        IDatabase database = service.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

        await Assert.ThrowsAsync<RedisServerException>(() => database.StringSetAsync("other:key", "x"));
        await using ConnectionMultiplexer admin = await ConnectAdminAsync("auth-service", SERVICE_PASSWORD);
        await Assert.ThrowsAsync<RedisServerException>(() => admin.GetDatabase().ExecuteAsync("FLUSHALL"));
        await Assert.ThrowsAsync<RedisServerException>(() => admin.GetDatabase().ExecuteAsync("KEYS", "*"));
    }

    [Fact]
    public async Task NoCredentials_DefaultUserIsOff()
    {
        await using ServiceProvider anonymous = Provider(user: null, password: null);
        IDatabase database = anonymous.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

        await Assert.ThrowsAnyAsync<Exception>(() => database.StringGetAsync($"auth:revoked:user:{SUBJECT}"));
    }

    private Task<ConnectionMultiplexer> ConnectAdminAsync(string user, string password) =>
        ConnectionMultiplexer.ConnectAsync($"{_redis!.Hostname}:{_redis.GetMappedPublicPort(6379)},user={user},password={password},allowAdmin=true");

    // Пакет отзыва так, как его подключает сервис: AddPlatformTokenRevocation и строка подключения с пользователем.
    private ServiceProvider Provider(string? user, string? password)
    {
        string connection = $"{_redis!.Hostname}:{_redis.GetMappedPublicPort(6379)}"
            + (user is null ? string.Empty : $",user={user},password={password}");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("ConnectionStrings:Redis", connection)])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new JwtAuthenticationOptions { Authority = "http://localhost:8180/realms/sandbox", Audience = "sandbox-api" }));
        services.AddPlatformTokenRevocation(configuration);
        return services.BuildServiceProvider();
    }
}
