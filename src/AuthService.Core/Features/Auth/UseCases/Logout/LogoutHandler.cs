using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Features.Auth.UseCases.Logout;

/// <summary>Завершение сессии в Keycloak. Уже закрытая сессия — тоже успешный выход.</summary>
public sealed class LogoutHandler(IIdentityProvider identityProvider)
{
    public async Task<UnitResult<Error>> Handle(string? refreshToken, CancellationToken cancellationToken)
    {
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
