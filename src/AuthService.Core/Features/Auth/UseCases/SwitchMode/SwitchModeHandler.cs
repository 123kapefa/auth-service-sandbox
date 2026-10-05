using AuthService.Contracts.Auth;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Core.Features.Admin;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Auth.UseCases.SwitchMode;

/// <summary>Режим переключён: ответ и новые токены (null — refresh-cookie нет, клиент обновит токен сам).</summary>
public sealed record ModeSwitched(SwitchModeResponse Response, TokenSet? Tokens);

/// <summary>
/// Переключение активного режима (роль = режим: любая роль пользователя в приложении). Доступные режимы — по свежим ролям из Keycloak, а не по токену: в токене
/// могут остаться снятые роли. Режим — атрибут active_mode пользователя; refresh выдаёт токен уже с новым режимом.
/// <para>
/// Режим в другом приложении проекта (App) — атрибут active_mode.{приложение}: его читает маппер токена приложения.
/// Токены приложения со старым режимом сразу перестают приниматься (умный рефреш): страница приложения обновит токен
/// и получит права нового режима без перелогина.
/// </para>
/// </summary>
public sealed class SwitchModeHandler(
    IIdentityAdmin identityAdmin,
    IIdentityProvider identityProvider,
    UserAppRoles userRoles,
    AccessChanges accessChanges,
    IValidator<SwitchModeRequest> validator,
    IOptions<JwtAuthenticationOptions> authentication)
{
    /// <summary>Режимы пользователя в приложениях проекта, где у него есть роли и доступ не закрыт.</summary>
    public async Task<Result<IReadOnlyList<AppModeResponse>, Error>> ListAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return AuthErrors.SessionMissing();
        }

        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(id, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error.Type == ErrorType.NOT_FOUND ? AuthErrors.SessionMissing() : user.Error;
        }

        Result<IReadOnlyList<AppAccessInfo>, Error> apps = await identityAdmin.GetAppAccessAsync(id, cancellationToken);
        if (apps.IsFailure)
        {
            return apps.Error;
        }

        List<AppModeResponse> modes = [];
        foreach (AppAccessInfo app in apps.Value.Where(app => app.HasAccess))
        {
            Result<UserRoles, Error> roles = await userRoles.LoadAsync(id, app.ClientId, cancellationToken);
            if (roles.IsFailure || roles.Value.Modes.Count == 0)
            {
                continue; // клиент без ролей — только вход, режимов нет
            }

            modes.Add(new AppModeResponse(app.ClientId, app.Name, roles.Value.Modes, ActiveModeOf(user.Value, app.ClientId, roles.Value)));
        }

        return modes;
    }

    public async Task<Result<ModeSwitched, Error>> Handle(
        Guid? userId,
        SwitchModeRequest request,
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (userId is not { } id)
        {
            return AuthErrors.SessionMissing();
        }

        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(id, cancellationToken);
        if (user.IsFailure)
        {
            // Пользователя удалили, а токен ещё жив — как в боевом сервисе, это 401.
            return user.Error.Type == ErrorType.NOT_FOUND ? AuthErrors.SessionMissing() : user.Error;
        }

        if (!user.Value.Enabled)
        {
            return AuthErrors.UserBlocked();
        }

        string app = string.IsNullOrWhiteSpace(request.App) ? authentication.Value.Audience : request.App.Trim();
        bool isOwnApp = string.Equals(app, authentication.Value.Audience, StringComparison.Ordinal);
        Result<UserRoles, Error> roles = await userRoles.LoadAsync(id, app, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        if (roles.Value.HasAccess == false)
        {
            return AuthErrors.ModeNotAvailable(); // доступ к приложению закрыт — режима в нём нет
        }

        // Роль = режим: режим — любая роль пользователя в приложении.
        string? mode = roles.Value.Modes.FirstOrDefault(
            available => string.Equals(available, request.ActiveMode!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (mode is null)
        {
            return AuthErrors.ModeNotAvailable();
        }

        string attribute = isOwnApp ? AuthClaims.ACTIVE_MODE : UserAttributes.AppModeOf(app);
        string? current = isOwnApp ? user.Value.ActiveMode : user.Value.AppModes?.GetValueOrDefault(app);
        if (!string.Equals(current, mode, StringComparison.Ordinal))
        {
            UnitResult<Error> saved = await identityAdmin.UpdateUserAsync(
                id,
                new UserChanges(null, new Dictionary<string, string?>(StringComparer.Ordinal) { [attribute] = mode }),
                cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            // Токен приложения несёт права старого режима — пусть приложение его обновит. Свой токен обновляется ниже.
            if (!isOwnApp)
            {
                UnitResult<Error> changed = await accessChanges.UserChangedAsync(id, cancellationToken);
                if (changed.IsFailure)
                {
                    return changed.Error;
                }
            }
        }

        TokenSet? tokens = null;
        if (refreshToken is not null)
        {
            Result<TokenSet, Error> refreshed = await identityProvider.RefreshAsync(refreshToken, cancellationToken);
            if (refreshed.IsFailure)
            {
                return refreshed.Error;
            }

            tokens = refreshed.Value;
        }

        Result<bool, Error> hasPassword = await identityAdmin.HasPasswordAsync(id, cancellationToken);
        if (hasPassword.IsFailure)
        {
            return hasPassword.Error;
        }

        var response = new SwitchModeResponse(
            id,
            user.Value.Phone,
            StaffRoles.Describe(roles.Value.Assigned),
            roles.Value.Modes,
            mode,
            hasPassword.Value);
        return new ModeSwitched(response, tokens);
    }

    // Режим не выбирали — роль по умолчанию (первая), как в маппере токена; её нет — первая роль.
    private string? ActiveModeOf(IdentityUser user, string app, UserRoles roles)
    {
        string? chosen = string.Equals(app, authentication.Value.Audience, StringComparison.Ordinal)
            ? user.ActiveMode
            : user.AppModes?.GetValueOrDefault(app);
        return chosen is not null && roles.Modes.Contains(chosen, StringComparer.Ordinal)
            ? chosen
            : roles.Defaults.Count > 0 ? roles.Defaults[0] : roles.Modes.Count > 0 ? roles.Modes[0] : null;
    }
}
