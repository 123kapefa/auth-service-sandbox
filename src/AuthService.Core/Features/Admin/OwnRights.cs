using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>
/// Правило «в пределах собственных прав»: менять можно только то, что есть у самого меняющего. Одно место для выдачи и
/// снятия ролей (12.4) и для прав ключей сервисов (12.10).
/// <para>
/// Права меняющего: в приложении сервиса — из его токена (активный режим); в другом приложении — по его действующим
/// ролям в нём. Не ограничиваем того, у кого есть roles.manage (в токене — админ проекта, во всех приложениях проекта; в
/// роли приложения — админ приложения): он и так определяет любую роль с любыми правами, запрет ему ничего бы не дал, а
/// честную работу (выдать роль с новым правом приложения) ломал. Правило — против того, у кого есть право выдавать
/// (users.roles.manage, services.manage), но нет roles.manage: он меняет только в пределах собственных прав.
/// </para>
/// </summary>
public sealed class OwnRights(IIdentityAdmin identityAdmin, IOptions<JwtAuthenticationOptions> authentication)
{
    /// <summary>
    /// Права из touched, которых нет у меняющего, в приложении clientId; пусто — всё в пределах его прав.
    /// </summary>
    /// <param name="actorId">sub меняющего; null — токена человека нет (в чужом приложении это отказ).</param>
    /// <param name="actorPermissions">Права из токена в активном режиме (приложение сервиса).</param>
    public async Task<Result<string[], Error>> MissingAsync(
        Guid? actorId,
        IReadOnlySet<string>? actorPermissions,
        string clientId,
        IReadOnlySet<string> touched,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(touched);
        if (touched.Count == 0)
        {
            return Result.Success<string[], Error>([]);
        }

        IReadOnlySet<string> own = actorPermissions ?? new HashSet<string>(StringComparer.Ordinal);
        if (own.Contains(AuthServicePermissions.Roles.MANAGE))
        {
            return Result.Success<string[], Error>([]);
        }

        if (!string.Equals(clientId, authentication.Value.Audience, StringComparison.Ordinal))
        {
            if (actorId is not { } id)
            {
                return Result.Success<string[], Error>([.. touched]);
            }

            Result<IReadOnlyList<string>, Error> effective = await identityAdmin.GetEffectiveClientRolesAsync(id, clientId, cancellationToken);
            if (effective.IsFailure)
            {
                return effective.Error;
            }

            own = new HashSet<string>(effective.Value, StringComparer.Ordinal);
            if (own.Contains(AuthServicePermissions.Roles.MANAGE))
            {
                return Result.Success<string[], Error>([]);
            }
        }

        return Result.Success<string[], Error>([.. touched.Where(permission => !own.Contains(permission))]);
    }
}
