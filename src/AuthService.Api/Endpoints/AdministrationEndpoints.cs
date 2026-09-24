using System.Security.Cryptography;
using AuthService.Contracts;
using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Api.Endpoints;

public static class AdministrationEndpoints
{
    public static IEndpointRouteBuilder MapAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin").RequireAuthorization("AdminMfa");
        group.MapGet("/clients", ListClientsAsync);
        group.MapPost("/clients", CreateClientAsync);
        group.MapPost("/clients/{clientId}/rotate-secret", RotateSecretAsync);
        group.MapGet("/scopes", ListScopesAsync);
        group.MapPost("/scopes", CreateScopeAsync);
        group.MapPut("/accounts/{accountId:guid}/platform-admin", SetPlatformAdminAsync);
        group.MapPost("/accounts/{accountId:guid}/revoke-sessions", RevokeSessionsAsync);
        return endpoints;
    }

    private static async Task<IResult> ListClientsAsync(IOpenIddictApplicationManager manager, CancellationToken cancellationToken)
    {
        var response = new List<ClientResponse>();
        await foreach (var application in manager.ListAsync(null, null, cancellationToken))
        {
            var clientId = await manager.GetClientIdAsync(application, cancellationToken);
            if (clientId is null)
            {
                continue;
            }

            response.Add(new ClientResponse(
                clientId,
                await manager.GetDisplayNameAsync(application, cancellationToken),
                ToClientKind(await manager.GetClientTypeAsync(application, cancellationToken)),
                (await manager.GetRedirectUrisAsync(application, cancellationToken)).ToArray(),
                (await manager.GetPermissionsAsync(application, cancellationToken))
                    .Where(permission => permission.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
                    .Select(permission => permission[Permissions.Prefixes.Scope.Length..])
                    .ToArray()));
        }

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateClientAsync(
        CreateClientRequest request,
        IOpenIddictApplicationManager manager,
        ISecurityAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!IsValidClient(request))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["client"] = ["Invalid client configuration."] });
        }

        if (await manager.FindByClientIdAsync(request.ClientId, cancellationToken) is not null)
        {
            return Results.Conflict(new { code = "client.exists" });
        }

        var secret = request.Kind is OAuthClientKind.Confidential or OAuthClientKind.Service ? GenerateSecret() : null;
        var descriptor = CreateDescriptor(request, secret);
        await manager.CreateAsync(descriptor, cancellationToken);
        await auditWriter.WriteAsync("admin.client_created", clientId: request.ClientId, cancellationToken: cancellationToken);
        return Results.Created($"/api/admin/clients/{request.ClientId}", secret is null
            ? new ClientResponse(request.ClientId, request.DisplayName, request.Kind, request.RedirectUris, request.AllowedScopes)
            : new ClientSecretResponse(request.ClientId, secret));
    }

    private static async Task<IResult> RotateSecretAsync(
        string clientId,
        IOpenIddictApplicationManager manager,
        ISecurityAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        var application = await manager.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null || await manager.HasClientTypeAsync(application, ClientTypes.Public, cancellationToken))
        {
            return Results.NotFound();
        }

        var secret = GenerateSecret();
        await manager.UpdateAsync(application, secret, cancellationToken);
        await auditWriter.WriteAsync("admin.client_secret_rotated", clientId: clientId, cancellationToken: cancellationToken);
        return Results.Ok(new ClientSecretResponse(clientId, secret));
    }

    private static async Task<IResult> ListScopesAsync(IOpenIddictScopeManager manager, CancellationToken cancellationToken)
    {
        var response = new List<ScopeResponse>();
        await foreach (var scope in manager.ListAsync(null, null, cancellationToken))
        {
            var name = await manager.GetNameAsync(scope, cancellationToken);
            if (name is null)
            {
                continue;
            }

            response.Add(new ScopeResponse(
                name,
                await manager.GetDisplayNameAsync(scope, cancellationToken),
                (await manager.GetResourcesAsync(scope, cancellationToken)).ToArray()));
        }

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateScopeAsync(
        CreateScopeRequest request,
        IOpenIddictScopeManager manager,
        ISecurityAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100 || request.Resources.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["scope"] = ["Scope name and at least one resource are required."] });
        }

        if (await manager.FindByNameAsync(request.Name, cancellationToken) is not null)
        {
            return Results.Conflict(new { code = "scope.exists" });
        }

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = request.Name,
            DisplayName = request.DisplayName
        };
        descriptor.Resources.UnionWith(request.Resources);
        await manager.CreateAsync(descriptor, cancellationToken);
        await auditWriter.WriteAsync("admin.scope_created", metadata: request.Name, cancellationToken: cancellationToken);
        return Results.Created($"/api/admin/scopes/{request.Name}", new ScopeResponse(request.Name, request.DisplayName, request.Resources));
    }

    private static async Task<IResult> SetPlatformAdminAsync(
        Guid accountId,
        SetPlatformRoleRequest request,
        UserManager<Account> userManager,
        ISecurityAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        var account = await userManager.FindByIdAsync(accountId.ToString());
        if (account is null)
        {
            return Results.NotFound();
        }

        var result = request.IsPlatformAdmin
            ? await userManager.AddToRoleAsync(account, "platform_admin")
            : await userManager.RemoveFromRoleAsync(account, "platform_admin");
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = result.Errors.Select(error => error.Code).ToArray() });
        }

        await userManager.UpdateSecurityStampAsync(account);
        await auditWriter.WriteAsync("admin.platform_role_changed", account.Id, metadata: request.IsPlatformAdmin.ToString(), cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeSessionsAsync(
        Guid accountId,
        UserManager<Account> userManager,
        IOpenIddictTokenManager tokenManager,
        ISecurityAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        var account = await userManager.FindByIdAsync(accountId.ToString());
        if (account is null)
        {
            return Results.NotFound();
        }

        await tokenManager.RevokeBySubjectAsync(account.Id.ToString(), cancellationToken);
        await userManager.UpdateSecurityStampAsync(account);
        await auditWriter.WriteAsync("admin.sessions_revoked", account.Id, cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static OpenIddictApplicationDescriptor CreateDescriptor(CreateClientRequest request, string? secret)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = request.ClientId,
            ClientSecret = secret,
            DisplayName = request.DisplayName,
            ClientType = request.Kind == OAuthClientKind.Public ? ClientTypes.Public : ClientTypes.Confidential,
            ConsentType = request.Trusted ? ConsentTypes.Implicit : ConsentTypes.Explicit
        };

        if (request.Kind == OAuthClientKind.Service)
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Token);
            descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        }
        else
        {
            descriptor.RedirectUris.UnionWith(request.RedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)));
            descriptor.PostLogoutRedirectUris.UnionWith(request.PostLogoutRedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)));
            descriptor.Permissions.UnionWith([
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code
            ]);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        foreach (var scope in request.AllowedScopes.Where(scope => !string.IsNullOrWhiteSpace(scope)))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        return descriptor;
    }

    private static bool IsValidClient(CreateClientRequest request) =>
        !string.IsNullOrWhiteSpace(request.ClientId) && request.ClientId.Length <= 100 &&
        !string.IsNullOrWhiteSpace(request.DisplayName) &&
        (request.Kind == OAuthClientKind.Service || request.RedirectUris.All(IsExactRedirectUri)) &&
        request.PostLogoutRedirectUris.All(IsExactRedirectUri);

    private static bool IsExactRedirectUri(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback)) &&
        !uri.Contains('*', StringComparison.Ordinal);

    private static OAuthClientKind ToClientKind(string? clientType) => clientType == ClientTypes.Public
        ? OAuthClientKind.Public
        : OAuthClientKind.Confidential;

    private static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
