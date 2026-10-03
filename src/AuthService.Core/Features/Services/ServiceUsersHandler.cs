using AuthService.Contracts.Services;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Admin;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Services;

/// <summary>
/// Что сервисы проекта по ключу доступа узнают о пользователях: карточку (service.users.read) и есть ли у пользователя
/// право в приложении сейчас (service.access.check) — для фоновых задач, где токена пользователя нет. Правило доступа —
/// как у токена: аккаунт не заблокирован, доступ к приложению открыт, право — в составе активного режима.
/// </summary>
public sealed class ServiceUsersHandler(
    IIdentityAdmin identityAdmin,
    UserAppRoles userRoles,
    IOptions<JwtAuthenticationOptions> authentication)
{
    public const string BLOCKED = "user.blocked";
    public const string ACCESS_DENIED = "app.access_denied";
    public const string NO_MODE = "app.no_mode";
    public const string PERMISSION_MISSING = "permission.missing";

    public async Task<Result<ServiceUserResponse, Error>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        return user.IsSuccess ? ToResponse(user.Value) : user.Error;
    }

    public async Task<Result<ServiceUserResponse, Error>> FindAsync(string? login, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return GeneralErrors.ValueIsRequired("login");
        }

        Result<IdentityUser?, Error> user = await identityAdmin.FindUserExactAsync(login.Trim(), cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        return user.Value is { } found ? ToResponse(found) : IdentityProviderErrors.UserNotFound();
    }

    public async Task<Result<AccessCheckResponse, Error>> CheckAsync(
        Guid userId,
        string? app,
        string? permission,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return GeneralErrors.ValueIsRequired("permission");
        }

        string clientId = string.IsNullOrWhiteSpace(app) ? authentication.Value.Audience : app.Trim();
        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        Result<UserRoles, Error> roles = await userRoles.LoadAsync(userId, clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        string? mode = string.Equals(clientId, authentication.Value.Audience, StringComparison.Ordinal)
            ? user.Value.ActiveMode
            : user.Value.AppModes?.GetValueOrDefault(clientId);
        if (!user.Value.Enabled)
        {
            return Denied(BLOCKED, mode);
        }

        if (roles.Value.HasAccess == false)
        {
            return Denied(ACCESS_DENIED, mode);
        }

        if (mode is null || !roles.Value.Modes.Contains(mode, StringComparer.Ordinal))
        {
            return Denied(NO_MODE, mode);
        }

        Result<IReadOnlyList<string>, Error> composites = await identityAdmin.GetRoleCompositesAsync(clientId, mode, cancellationToken);
        if (composites.IsFailure)
        {
            return composites.Error;
        }

        // В составе роли режима — права (и, возможно, вложенные роли): права — всё, что не роль приложения.
        HashSet<string> appRoles = [.. roles.Value.Catalog.Where(role => !AppCatalog.IsPermission(role)).Select(role => role.Name)];
        string[] permissions = [.. composites.Value.Where(name => !appRoles.Contains(name)).Order(StringComparer.Ordinal)];
        bool allowed = permissions.Contains(permission.Trim(), StringComparer.Ordinal);
        return new AccessCheckResponse(allowed, allowed ? null : PERMISSION_MISSING, mode, permissions);
    }

    private static AccessCheckResponse Denied(string reason, string? mode) => new(false, reason, mode, []);

    private static ServiceUserResponse ToResponse(IdentityUser user) =>
        new(user.Id, user.Username, user.Phone, user.Email, user.Enabled, user.RestrictionReason, user.CreatedAt);
}
