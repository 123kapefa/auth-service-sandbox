using AuthService.Contracts.Me;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Auth;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Me;

/// <summary>
/// GET /me/security — раздел «Безопасность» кабинета: пароль, приложения-аутентификаторы, ключи доступа, коды
/// восстановления (без секретов). Подключить и удалить — на страницах сервера входа (/auth/login?action=…).
/// Любой вошедший — только своё.
/// </summary>
public sealed class GetSecurityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/me/security", HandleAsync).RequireUser();

    private static async Task<EndpointResult<SecurityResponse>> HandleAsync(
        [FromServices] CurrentUser user,
        [FromServices] IIdentityAdmin identityAdmin,
        CancellationToken cancellationToken)
    {
        if (user.Id is not { } id)
        {
            return Result.Failure<SecurityResponse, Error>(AuthErrors.SessionMissing());
        }

        Result<IReadOnlyList<UserCredential>, Error> credentials = await identityAdmin.GetCredentialsAsync(id, cancellationToken);
        return credentials.Map(ToResponse);
    }

    private static SecurityResponse ToResponse(IReadOnlyList<UserCredential> credentials)
    {
        // Набор кодов у Keycloak один (новые заменяют старые); на всякий случай — самый новый.
        UserCredential? codes = credentials
            .Where(credential => IsOf(credential, Credentials.RECOVERY_CODES))
            .MaxBy(credential => credential.CreatedAt);
        return new SecurityResponse(
            credentials.Any(credential => IsOf(credential, Credentials.PASSWORD)),
            Of(credentials, Credentials.OTP),
            Of(credentials, Credentials.PASSKEY),
            codes is null ? null : new RecoveryCodesResponse(codes.Id, codes.CreatedAt, codes.Remaining, codes.Total));
    }

    private static List<CredentialResponse> Of(IReadOnlyList<UserCredential> credentials, string type) =>
        [.. credentials
            .Where(credential => IsOf(credential, type))
            .OrderBy(credential => credential.CreatedAt)
            .Select(credential => new CredentialResponse(credential.Id, credential.Label, credential.CreatedAt))];

    private static bool IsOf(UserCredential credential, string type) => string.Equals(credential.Type, type, StringComparison.Ordinal);
}
