using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using PlatformAuth.Revocation;
using SharedKernel;

namespace AuthService.Core.Features.Auth.UseCases.BackchannelLogout;

/// <summary>
/// Keycloak сообщил о завершённой у него сессии (выход в личном кабинете, в админке Keycloak, истечение сессии):
/// её уже выданные access-токены отзываются сразу. Нет sid — отзываются все токены пользователя.
/// </summary>
public sealed class BackchannelLogoutHandler(
    IIdentityProvider identityProvider,
    ITokenRevocationStore revocations,
    TimeProvider timeProvider)
{
    public async Task<UnitResult<Error>> Handle(string? logoutToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logoutToken))
        {
            return GeneralErrors.ValueIsRequired("logout_token");
        }

        Result<LogoutNotice, Error> notice = await identityProvider.ValidateLogoutTokenAsync(logoutToken, cancellationToken);
        if (notice.IsFailure)
        {
            return notice.Error;
        }

        if (notice.Value.SessionId is { } sessionId)
        {
            await revocations.RevokeSessionAsync(sessionId, cancellationToken);
        }
        else
        {
            await revocations.RevokeUserAsync(notice.Value.Subject!, timeProvider.GetUtcNow(), cancellationToken);
        }

        return UnitResult.Success<Error>();
    }
}
