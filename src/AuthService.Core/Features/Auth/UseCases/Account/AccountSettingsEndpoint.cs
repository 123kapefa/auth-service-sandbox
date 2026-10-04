using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AuthService.Core.Features.Auth.UseCases.Account;

/// <summary>
/// Постоянные ссылки для приложений проекта (адреса страниц могут меняться, эти — нет):
/// GET /auth/account — личный кабинет сервиса входа (/account/): профиль, пароль и почта;
/// GET /auth/account/security — его раздел «Безопасность»: 2FA, коды восстановления, ключи доступа;
/// GET /auth/account/devices — раздел «Устройства»: где вошли, выйти на устройстве или везде, история входов.
/// </summary>
public sealed class AccountSettingsEndpoint : IEndpoint
{
    public const string ACCOUNT_PATH = "/account/";
    public const string SECURITY_PATH = "/account/security";
    public const string DEVICES_PATH = "/account/devices";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/account", () => Results.Redirect(ACCOUNT_PATH));
        app.MapGet("/auth/account/security", () => Results.Redirect(SECURITY_PATH));
        app.MapGet("/auth/account/devices", () => Results.Redirect(DEVICES_PATH));
    }
}
