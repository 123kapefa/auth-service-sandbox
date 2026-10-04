using System.Security.Cryptography;
using System.Text;
using AuthService.Contracts.Auth;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Auth.Cookies;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Auth.UseCases.Callback;

/// <summary>Вход завершён: токены для cookie и куда вернуть пользователя.</summary>
public sealed record LoginCompleted(TokenSet Tokens, string ReturnTo);

/// <summary>Возврат со страницы входа: проверка state, обмен кода на токены с PKCE verifier.</summary>
public sealed class CallbackHandler(IIdentityProvider identityProvider, IValidator<CallbackRequest> validator)
{
    private const string ACTION_STATUS = "action_status";

    public async Task<Result<LoginCompleted, Error>> Handle(
        CallbackRequest request,
        LoginState? savedState,
        string? actionStatus,
        Uri callbackUrl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (savedState is null)
        {
            return AuthErrors.LoginStateMissing();
        }

        // Пользователь отменил вход или сервер входа отказал (access_denied и т.п.).
        if (!string.IsNullOrEmpty(request.Error))
        {
            return AuthErrors.LoginDenied(request.ErrorDescription ?? request.Error);
        }

        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        // Защита от подделки callback (CSRF): state должен совпасть с тем, что сохранили при уходе на вход.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(request.State!),
                Encoding.UTF8.GetBytes(savedState.State)))
        {
            return AuthErrors.LoginStateMismatch();
        }

        Result<TokenSet, Error> tokens = await identityProvider.ExchangeCodeAsync(
            request.Code!,
            savedState.CodeVerifier,
            callbackUrl,
            cancellationToken);

        return tokens.IsSuccess ? new LoginCompleted(tokens.Value, WithActionStatus(savedState.ReturnTo, actionStatus)) : tokens.Error;
    }

    // Итог действия — параметром адреса возврата (перед #фрагментом); неизвестные значения не передаём. Свой
    // action_status в return_to (подложенный в ссылку) убираем всегда: итог сообщает только сервер входа.
    private static string WithActionStatus(string returnTo, string? actionStatus)
    {
        int hash = returnTo.IndexOf('#', StringComparison.Ordinal);
        string beforeHash = hash < 0 ? returnTo : returnTo[..hash];
        string fragment = hash < 0 ? string.Empty : returnTo[hash..];
        int question = beforeHash.IndexOf('?', StringComparison.Ordinal);
        string path = question < 0 ? beforeHash : beforeHash[..question];
        List<string> parameters = question < 0
            ? []
            : [.. beforeHash[(question + 1)..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(parameter => !string.Equals(parameter.Split('=')[0], ACTION_STATUS, StringComparison.Ordinal))];

        if (actionStatus is not null && AccountActions.Statuses.Contains(actionStatus))
        {
            parameters.Add($"{ACTION_STATUS}={actionStatus}");
        }

        return parameters.Count == 0 ? path + fragment : $"{path}?{string.Join('&', parameters)}{fragment}";
    }
}
