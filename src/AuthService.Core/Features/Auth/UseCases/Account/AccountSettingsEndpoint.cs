using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth;

namespace AuthService.Core.Features.Auth.UseCases.Account;

/// <summary>
/// Постоянные ссылки для приложений проекта (адреса страниц могут меняться, эти — нет):
/// GET /auth/account — личный кабинет сервиса входа (/account/): профиль, пароль и почта;
/// GET /auth/account/security — 2FA и ключи доступа. Пока свой раздел не готов (этап 6б.2) — раздел «Вход» кабинета
/// Keycloak (адрес — от Authentication:Authority текущего окружения).
/// </summary>
public sealed class AccountSettingsEndpoint : IEndpoint
{
    public const string ACCOUNT_PATH = "/account/";
    public const string KEYCLOAK_SIGNING_IN_PATH = "/account/account-security/signing-in";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/account", () => Results.Redirect(ACCOUNT_PATH));
        app.MapGet("/auth/account/security", ([FromServices] IOptions<JwtAuthenticationOptions> realm) =>
            Results.Redirect(realm.Value.Authority.TrimEnd('/') + KEYCLOAK_SIGNING_IN_PATH));
    }
}
