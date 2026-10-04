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
        services.AddScoped<UserRolesHandler>();
        services.AddScoped<AppUsersHandler>();
        services.AddScoped<ManagedApps>();
        services.AddScoped<CredentialsHandler>();
        services.AddHostedService<PermissionCatalogSync>();
        services.AddScoped<SessionsHandler>();
        services.AddScoped<LoginDevices>();
        services.AddScoped<LoginDeviceRecorder>();
        services.AddScoped<LoginHistoryHandler>();
        services.AddScoped<ServiceKeysHandler>();
        services.AddScoped<ServiceUsersHandler>();
        services.AddScoped<BrandingHandler>();

        return services;
    }
}
