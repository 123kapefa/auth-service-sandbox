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
/// </summary>
public sealed class SwitchModeHandler(
    IIdentityAdmin identityAdmin,
    IIdentityProvider identityProvider,
    UserAppRoles userRoles,
    IValidator<SwitchModeRequest> validator,
    IOptions<JwtAuthenticationOptions> authentication)
{
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

        Result<UserRoles, Error> roles = await userRoles.LoadAsync(id, authentication.Value.Audience, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        // Роль = режим: режим — любая роль пользователя в приложении.
        string? mode = roles.Value.Modes.FirstOrDefault(
            available => string.Equals(available, request.ActiveMode!.Trim(), StringComparison.OrdinalIgnoreCase));
        if (mode is null)
        {
            return AuthErrors.ModeNotAvailable();
        }

        if (!string.Equals(user.Value.ActiveMode, mode, StringComparison.Ordinal))
        {
            UnitResult<Error> saved = await identityAdmin.UpdateUserAsync(
                id,
                new UserChanges(null, new Dictionary<string, string?>(StringComparer.Ordinal) { [AuthClaims.ACTIVE_MODE] = mode }),
                cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
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
}
