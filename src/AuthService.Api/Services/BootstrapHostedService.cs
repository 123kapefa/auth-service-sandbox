using AuthService.Api.Options;
using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Api.Services;

public sealed class BootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<BootstrapOptions> options,
    IOptions<OidcOptions> oidcOptions,
    IHostEnvironment environment,
    ILogger<BootstrapHostedService> logger) : IHostedService
{
    private static readonly Action<ILogger, Exception?> LogIncompleteBootstrap = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, "BootstrapCredentialsIncomplete"),
        "Bootstrap is enabled but Bootstrap admin credentials are incomplete.");

    private static readonly Action<ILogger, string, Exception?> LogBootstrapFailure = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(2, "BootstrapAdminCreationFailed"),
        "Bootstrap admin could not be created: {Errors}");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var roleManager = services.GetRequiredService<RoleManager<Role>>();
        var scopeManager = services.GetRequiredService<IOpenIddictScopeManager>();
        var applicationManager = services.GetRequiredService<IOpenIddictApplicationManager>();

        if (!await roleManager.RoleExistsAsync("platform_admin"))
        {
            await roleManager.CreateAsync(new Role("platform_admin"));
        }

        await EnsureScopeAsync(scopeManager, "auth.admin", "Auth administration", "auth-api", cancellationToken);
        await EnsureScopeAsync(scopeManager, "demo.api", "Demo API", "demo-api", cancellationToken);

        if (environment.IsDevelopment())
        {
            await EnsureDevelopmentClientsAsync(applicationManager, options.Value, cancellationToken);
        }

        if (!options.Value.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Value.AdminEmail) ||
            string.IsNullOrWhiteSpace(options.Value.AdminUserName) ||
            string.IsNullOrWhiteSpace(options.Value.AdminPassword))
        {
            LogIncompleteBootstrap(logger, null);
            return;
        }

        var userManager = services.GetRequiredService<UserManager<Account>>();
        var emailSender = services.GetRequiredService<IEmailSender>();
        var auditWriter = services.GetRequiredService<ISecurityAuditWriter>();
        if (await userManager.FindByEmailAsync(options.Value.AdminEmail) is not null)
        {
            return;
        }

        var account = new Account(options.Value.AdminEmail, options.Value.AdminUserName, "Platform administrator");
        var result = await userManager.CreateAsync(account, options.Value.AdminPassword);
        if (!result.Succeeded)
        {
            LogBootstrapFailure(logger, string.Join(",", result.Errors.Select(error => error.Code)), null);
            return;
        }

        await userManager.AddToRoleAsync(account, "platform_admin");
        var token = await userManager.GenerateEmailConfirmationTokenAsync(account);
        var confirmationLink = $"{oidcOptions.Value.Issuer.TrimEnd('/')}/Account/ConfirmEmail?userId={account.Id}&code={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(account.Email!, "Подтвердите bootstrap admin", $"<p><a href=\"{confirmationLink}\">Подтвердить email</a></p>", cancellationToken);
        await auditWriter.WriteAsync("bootstrap.admin_created", account.Id, cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureScopeAsync(
        IOpenIddictScopeManager manager,
        string name,
        string displayName,
        string resource,
        CancellationToken cancellationToken)
    {
        if (await manager.FindByNameAsync(name, cancellationToken) is not null)
        {
            return;
        }

        var descriptor = new OpenIddictScopeDescriptor { Name = name, DisplayName = displayName };
        descriptor.Resources.Add(resource);
        await manager.CreateAsync(descriptor, cancellationToken);
    }

    private static async Task EnsureDevelopmentClientsAsync(
        IOpenIddictApplicationManager manager,
        BootstrapOptions bootstrap,
        CancellationToken cancellationToken)
    {
        if (await manager.FindByClientIdAsync("dev-spa", cancellationToken) is null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "dev-spa",
                DisplayName = "Development SPA",
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit,
                RedirectUris = { new Uri("http://localhost:5173/callback") },
                PostLogoutRedirectUris = { new Uri("http://localhost:5173/") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.EndSession,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Prefixes.Scope + "profile",
                    Permissions.Prefixes.Scope + "email",
                    Permissions.Prefixes.Scope + "roles",
                    Permissions.Prefixes.Scope + "demo.api"
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange }
            }, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(bootstrap.DevServiceSecret) &&
            await manager.FindByClientIdAsync("dev-service", cancellationToken) is null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "dev-service",
                ClientSecret = bootstrap.DevServiceSecret,
                DisplayName = "Development service",
                ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Implicit,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + "demo.api"
                }
            }, cancellationToken);
        }
    }
}
