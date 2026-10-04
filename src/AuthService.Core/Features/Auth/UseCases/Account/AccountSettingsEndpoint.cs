using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AuthService.Core.Features.Auth.UseCases.Account;

/// <summary>
/// Постоянные ссылки для приложений проекта (адреса страниц могут меняться, эти — нет):
/// GET /auth/account — личный кабинет сервиса входа (/account/): профиль, пароль и почта;
/// GET /auth/account/security — его раздел «Безопасность»: 2FA, коды восстановления, ключи доступа.
/// </summary>
public sealed class AccountSettingsEndpoint : IEndpoint
{
    public const string ACCOUNT_PATH = "/account/";
    public const string SECURITY_PATH = "/account/security";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/account", () => Results.Redirect(ACCOUNT_PATH));
        app.MapGet("/auth/account/security", () => Results.Redirect(SECURITY_PATH));
    }
}
