using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;
using PlatformAuth.Revocation;

namespace AuthService.Core.Features.Auth.UseCases.Logout;

/// <summary>Завершение сессии в Keycloak. Уже закрытая сессия — тоже успешный выход.</summary>
public sealed class LogoutHandler(IIdentityProvider identityProvider, ITokenRevocationStore revocations)
{
    /// <param name="refreshToken">refresh-токен из cookie: по нему Keycloak завершает сессию.</param>
    /// <param name="sessionId">sid текущего access-токена: его токены отзываются сразу (null — токена нет или истёк).</param>
    /// <param name="cancellationToken">Отмена запроса.</param>
    public async Task<UnitResult<Error>> Handle(string? refreshToken, string? sessionId, CancellationToken cancellationToken)
    {
        if (sessionId is not null)
        {
            await revocations.RevokeSessionAsync(sessionId, cancellationToken);
        }

        if (refreshToken is null)
        {
            return UnitResult.Success<Error>();
        }

        UnitResult<Error> logout = await identityProvider.LogoutAsync(refreshToken, cancellationToken);
        return logout.IsFailure && logout.Error.Type == ErrorType.AUTHENTICATION
            ? UnitResult.Success<Error>()
            : logout;
    }
}
