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
    public async Task<Result<LoginCompleted, Error>> Handle(
        CallbackRequest request,
        LoginState? savedState,
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

        return tokens.IsSuccess ? new LoginCompleted(tokens.Value, savedState.ReturnTo) : tokens.Error;
    }
}
