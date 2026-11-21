using AuthService.Core.Features.Admin;
using AuthService.Core.Features.Admin.Catalog;
using AuthService.Core.Features.Admin.UseCases.AppAccess;
using AuthService.Core.Features.Admin.UseCases.AppUserRoles;
using AuthService.Core.Features.Admin.UseCases.AppUsers;
using AuthService.Core.Features.Admin.UseCases.BrandingSettings;
using AuthService.Core.Features.Admin.UseCases.GetUser;
using AuthService.Core.Features.Admin.UseCases.GetUsers;
using AuthService.Core.Features.Admin.UseCases.Journal;
using AuthService.Core.Features.Admin.UseCases.Permissions;
using AuthService.Core.Features.Admin.UseCases.RoleCatalog;
using AuthService.Core.Features.Admin.UseCases.SetRestriction;
using AuthService.Core.Features.Admin.UseCases.ServiceKeys;
using AuthService.Core.Features.Admin.UseCases.SetRole;
using AuthService.Core.Features.Admin.UseCases.SignInCodes;
using AuthService.Core.Features.Admin.UseCases.UserCredentials;
using AuthService.Core.Features.Auth.Cookies;
using AuthService.Core.Features.Auth.UseCases.BackchannelLogout;
using AuthService.Core.Features.Auth.ReturnUrls;
using AuthService.Core.Features.Auth.UseCases.Callback;
using AuthService.Core.Features.Auth.UseCases.Login;
using AuthService.Core.Features.Auth.UseCases.Logout;
using AuthService.Core.Features.Auth.UseCases.Refresh;
using AuthService.Core.Features.Auth.UseCases.SwitchMode;
using AuthService.Core.Features.Security;
using AuthService.Core.Features.SignInMethods;
using AuthService.Core.Features.Services;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Core;

/// <summary>Регистрация слоя Core: эндпоинты (IEndpoint), валидаторы, хендлеры сценариев и службы входа.</summary>
public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        RejectCookieDomain(configuration);
        services.AddEndpoints(typeof(Registration).Assembly);

        services.Configure<AuthCookieOptions>(configuration.GetSection(AuthCookieOptions.SECTION_NAME));
        services.Configure<ReturnUrlOptions>(configuration.GetSection(ReturnUrlOptions.SECTION_NAME));
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuthCookieWriter>();
        services.AddSingleton<LoginStateStore>();
        services.AddSingleton<ReturnUrlValidator>();

        services.AddValidatorsFromAssembly(typeof(Registration).Assembly, includeInternalTypes: true);
        services.AddScoped<LoginHandler>();
        services.AddScoped<CallbackHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<BackchannelLogoutHandler>();
        services.AddScoped<SwitchModeHandler>();
        services.AddScoped<GetUsersHandler>();
        services.AddScoped<GetUserHandler>();
        services.AddScoped<SetRestrictionHandler>();
        services.AddScoped<SetRoleHandler>();
        services.AddScoped<AppAccessHandler>();
        services.AddScoped<AdminTargets>();
        services.AddScoped<AccessRevoker>();
        services.AddScoped<AccessChanges>();
        services.AddScoped<AdminAudit>();
        services.AddScoped<JournalHandler>();
        services.AddScoped<PermissionsHandler>();
        services.AddScoped<RolesHandler>();
        services.AddScoped<AppCatalog>();
        services.AddScoped<UserAppRoles>();
        services.AddScoped<OwnRights>();
        services.AddScoped<UserRolesHandler>();
        services.AddScoped<AppUsersHandler>();
        services.AddScoped<ManagedApps>();
        services.AddScoped<CredentialsHandler>();
        services.AddScoped<SignInMethodsHandler>();
        services.AddHostedService<PermissionCatalogSync>();
        services.AddScoped<SessionsHandler>();
        services.AddScoped<LoginDevices>();
        services.AddScoped<LoginDeviceRecorder>();
        services.AddScoped<Features.Phone.PhoneChangeRecorder>();
        services.AddScoped<LoginHistoryHandler>();
        services.AddScoped<ServiceKeysHandler>();
        services.AddScoped<ServiceUsersHandler>();
        services.AddScoped<AppCatalogHandler>();
        services.AddScoped<BrandingHandler>();
        services.AddScoped<AppBrandingHandler>();
        services.AddScoped<CodeSettingsHandler>();
        services.AddScoped<ProjectSignInHandler>();

        return services;
    }

    // Cookies:Domain убран (задача 12.1): с родительским доменом токены ушли бы на все поддомены проекта. Молча игнорировать
    // нельзя — оператор решил бы, что общий вход по cookie работает, — поэтому сервис не стартует и объясняет, что делать.
    private static void RejectCookieDomain(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration[AuthCookieOptions.SECTION_NAME + ":Domain"]))
        {
            throw new InvalidOperationException(
                "Cookies:Domain больше не поддерживается: cookie с токенами только для хоста сервиса входа, иначе любое приложение "
                + "на поддомене получило бы сессию человека. Уберите Cookies__Domain (AUTH_COOKIES_DOMAIN); приложениям проекта — "
                + "вход по PKCE (/sdk/platform-login.js) и токен в заголовке Authorization.");
        }
    }
}
