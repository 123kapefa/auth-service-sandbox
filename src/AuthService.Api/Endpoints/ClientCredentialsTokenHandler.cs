using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Api.Endpoints;

public sealed class ClientCredentialsTokenHandler(IOpenIddictScopeManager scopeManager)
    : IOpenIddictServerHandler<OpenIddictServerEvents.HandleTokenRequestContext>
{
    public async ValueTask HandleAsync(OpenIddictServerEvents.HandleTokenRequestContext context)
    {
        if (!context.Request.IsClientCredentialsGrantType())
        {
            return;
        }

        var clientId = context.Request.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            context.Reject(error: Errors.InvalidClient, description: "A client identifier is required.");
            return;
        }

        var identity = new ClaimsIdentity("Bearer", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, clientId);
        identity.SetClaim(Claims.ClientId, clientId);
        identity.SetScopes(context.Request.GetScopes());

        var resources = new List<string>();
        await foreach (var resource in scopeManager.ListResourcesAsync(identity.GetScopes()))
        {
            resources.Add(resource);
        }

        identity.SetResources(resources);
        identity.SetDestinations(_ => [Destinations.AccessToken]);
        context.SignIn(new ClaimsPrincipal(identity));
    }
}
