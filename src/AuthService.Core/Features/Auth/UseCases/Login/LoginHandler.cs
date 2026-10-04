using AuthService.Core.Abstractions;
using AuthService.Core.Features.Auth.Cookies;
using AuthService.Core.Features.Auth.ReturnUrls;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Features.Auth.UseCases.Login;

/// <summary>Результат начала входа: куда отправить браузер и что запомнить до его возврата.</summary>
public sealed record LoginStart(Uri RedirectTo, LoginState State);

/// <summary>Начало входа: адрес страницы входа с PKCE и проверенный return_to; action — действие с учётной записью.</summary>
public sealed class LoginHandler(IIdentityProvider identityProvider, ReturnUrlValidator returnUrls)
{
    public async Task<Result<LoginStart, Error>> Handle(
        string? returnTo,
        bool reauthenticate,
        string? action,
        Uri callbackUrl,
        CancellationToken cancellationToken)
    {
        AccountAction? accountAction = null;
        if (!string.IsNullOrEmpty(action))
        {
            if (!AccountActions.TryParse(action, out AccountAction parsed))
            {
                return AuthErrors.UnknownAccountAction(action);
            }

            accountAction = parsed;
        }

        Result<AuthorizationRequest, Error> request =
            await identityProvider.CreateAuthorizationRequestAsync(callbackUrl, reauthenticate, accountAction, cancellationToken);
        if (request.IsFailure)
        {
            return request.Error;
        }

        return new LoginStart(
            request.Value.Url,
            new LoginState(request.Value.State, request.Value.CodeVerifier, returnUrls.Resolve(returnTo)));
    }
}
