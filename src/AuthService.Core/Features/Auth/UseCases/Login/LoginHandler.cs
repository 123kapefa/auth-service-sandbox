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
        string? credential,
        Uri callbackUrl,
        CancellationToken cancellationToken)
    {
        AccountActionRequest? accountAction = null;
        if (!string.IsNullOrEmpty(action))
        {
            if (!AccountActions.TryParse(action, out AccountAction parsed))
            {
                return AuthErrors.UnknownAccountAction(action);
            }

            // Удалить — что именно: Id учётных данных строго в виде GUID (xxxxxxxx-xxxx-…), иначе в kc_action попал бы
            // произвольный текст; дальше уходит его каноническая запись.
            Guid credentialId = Guid.Empty;
            if (parsed == AccountAction.DeleteCredential && !Guid.TryParseExact(credential, "D", out credentialId))
            {
                return AuthErrors.CredentialRequired();
            }

            accountAction = new AccountActionRequest(
                parsed,
                parsed == AccountAction.DeleteCredential ? credentialId.ToString("D") : null);
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
