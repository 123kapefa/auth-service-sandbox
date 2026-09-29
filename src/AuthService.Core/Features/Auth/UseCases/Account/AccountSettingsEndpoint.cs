using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth;

namespace AuthService.Core.Features.Auth.UseCases.Account;

/// <summary>
/// GET /auth/account — в личный кабинет Keycloak, раздел «Вход»: там пользователь сам меняет пароль,
/// включает 2FA и добавляет ключ доступа. Адрес кабинета — от Authentication:Authority текущего окружения.
/// </summary>
public sealed class AccountSettingsEndpoint : IEndpoint
{
    public const string SIGNING_IN_PATH = "/account/account-security/signing-in";

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/auth/account", ([FromServices] IOptions<JwtAuthenticationOptions> realm) =>
            Results.Redirect(realm.Value.Authority.TrimEnd('/') + SIGNING_IN_PATH));
}
