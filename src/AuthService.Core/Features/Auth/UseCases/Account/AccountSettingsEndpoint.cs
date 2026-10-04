using AuthService.Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

namespace AuthService.Core.Features.Auth.UseCases.Account;

/// <summary>
/// Постоянные ссылки для приложений проекта (адреса страниц могут меняться, эти — нет):
/// GET /auth/account — личный кабинет сервиса входа (/account/): профиль, пароль и почта;
/// GET /auth/account/security — его раздел «Безопасность»: 2FA, коды восстановления, ключи доступа;
/// GET /auth/account/devices — раздел «Устройства»: где вошли, выйти на устройстве или везде, история входов.
/// ?app=clientId — кабинет открыт из приложения: он в оформлении приложения (название, логотип, цвет).
/// </summary>
public sealed class AccountSettingsEndpoint : IEndpoint
{
    public const string ACCOUNT_PATH = "/account/";
    public const string SECURITY_PATH = "/account/security";
    public const string DEVICES_PATH = "/account/devices";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/account", ([FromQuery(Name = "app")] string? from) => Redirect(ACCOUNT_PATH, from));
        app.MapGet("/auth/account/security", ([FromQuery(Name = "app")] string? from) => Redirect(SECURITY_PATH, from));
        app.MapGet("/auth/account/devices", ([FromQuery(Name = "app")] string? from) => Redirect(DEVICES_PATH, from));
    }

    private static IResult Redirect(string path, string? app) =>
        BrandingApps.Normalize(app) is { } valid
            ? Results.Redirect(QueryHelpers.AddQueryString(path, "app", valid))
            : Results.Redirect(path);
}
