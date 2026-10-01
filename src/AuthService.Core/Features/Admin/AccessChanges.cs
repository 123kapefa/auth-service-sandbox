using System.Globalization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using PlatformAuth.Revocation;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>
/// Умный рефреш: доступ изменился — сессии живы, но старые токены больше не принимаются (401 access_changed), и клиент
/// обновляет токен без перелогина. Версия — время изменения в миллисекундах: её пишем в Keycloak (маппер кладёт её
/// в токен) и в Redis; токен с версией меньше текущей устарел. Порядок — сначала Keycloak, потом Redis: токен, выданный
/// между ними, уже с новыми ролями, а лишний рефреш ничего не ломает. Понижение прав действует сразу.
/// </summary>
public sealed class AccessChanges(IIdentityAdmin identityAdmin, ITokenRevocationStore revocations, TimeProvider timeProvider)
{
    /// <summary>Роли пользователя изменились: обновить его токены (все приложения и сессии).</summary>
    public async Task<UnitResult<Error>> UserChangedAsync(Guid userId, CancellationToken cancellationToken)
    {
        long version = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        UnitResult<Error> saved = await identityAdmin.UpdateUserAsync(
            userId,
            new UserChanges(null, new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [UserAttributes.ACCESS_VERSION] = version.ToString(CultureInfo.InvariantCulture),
            }),
            cancellationToken);
        if (saved.IsFailure)
        {
            return saved;
        }

        await revocations.MarkAccessChangedAsync(userId.ToString(), version, cancellationToken);
        return UnitResult.Success<Error>();
    }

    /// <summary>Состав роли изменился: обновить токены всех, кто работает в этом режиме.</summary>
    public async Task<UnitResult<Error>> RoleChangedAsync(string clientId, AppRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);

        long version = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        UnitResult<Error> saved = await identityAdmin.UpdateAppRoleAsync(clientId, role with { Version = version }, cancellationToken);
        if (saved.IsFailure)
        {
            return saved;
        }

        await revocations.MarkModeChangedAsync(clientId, role.Name, version, cancellationToken);
        return UnitResult.Success<Error>();
    }

    /// <summary>Роль удалена: токены в этом режиме устарели (новые — уже без её прав).</summary>
    public Task RoleDeletedAsync(string clientId, string role, CancellationToken cancellationToken) =>
        revocations.MarkModeChangedAsync(clientId, role, timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), cancellationToken);
}
