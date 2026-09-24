using System.Security.Claims;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthService.Api.Endpoints;

public static class OpenIdConnectEndpoints
{
    public static IEndpointRouteBuilder MapOpenIdConnectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync);
        endpoints.MapMethods("/connect/userinfo", [HttpMethods.Get, HttpMethods.Post], (Delegate)UserInfoAsync)
            .RequireAuthorization("UserInfo");
        endpoints.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], (Delegate)LogoutAsync);
        return endpoints;
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext context,
        UserManager<Account> userManager,
        IOpenIddictScopeManager scopeManager,
        CancellationToken cancellationToken)
    {
        var request = context.GetOpenIddictServerRequest();
        if (request is null)
        {
            return Results.BadRequest();
        }

        var ticket = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!ticket.Succeeded || ticket.Principal is null)
        {
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString },
                [IdentityConstants.ApplicationScheme]);
        }

        var account = await userManager.GetUserAsync(ticket.Principal);
        if (account is null || !account.EmailConfirmed || await userManager.IsLockedOutAsync(account))
        {
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString },
                [IdentityConstants.ApplicationScheme]);
        }

        var identity = new ClaimsIdentity("Bearer", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, account.Id.ToString());
        identity.SetClaim(Claims.Name, account.UserName);
        identity.SetClaim(Claims.PreferredUsername, account.UserName);
        identity.SetClaim(Claims.Email, account.Email);
        identity.SetClaim("email_verified", account.EmailConfirmed ? "true" : "false");

        foreach (var role in await userManager.GetRolesAsync(account))
        {
            identity.AddClaim(new Claim(Claims.Role, role));
        }

        if (await userManager.GetTwoFactorEnabledAsync(account))
        {
            identity.AddClaim(new Claim("amr", "mfa"));
        }

        identity.SetScopes(request.GetScopes());
        var resources = new List<string>();
        await foreach (var resource in scopeManager.ListResourcesAsync(identity.GetScopes(), cancellationToken))
        {
            resources.Add(resource);
        }

        identity.SetResources(resources);
        identity.SetDestinations(GetDestinations);

        return Results.SignIn(
            new ClaimsPrincipal(identity),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static Task<IResult> UserInfoAsync(HttpContext context)
    {
        var user = context.User;
        var response = new Dictionary<string, object?>
        {
            [Claims.Subject] = user.GetClaim(Claims.Subject)
        };

        if (user.HasScope(Scopes.Profile))
        {
            response[Claims.Name] = user.GetClaim(Claims.Name);
            response[Claims.PreferredUsername] = user.GetClaim(Claims.PreferredUsername);
        }

        if (user.HasScope(Scopes.Email))
        {
            response[Claims.Email] = user.GetClaim(Claims.Email);
            response["email_verified"] = user.GetClaim("email_verified") == "true";
        }

        if (user.HasScope(Scopes.Roles))
        {
            response[Claims.Role] = user.GetClaims(Claims.Role);
        }

        return Task.FromResult(Results.Ok(response));
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.SignOut(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        if (claim.Type == Claims.Subject || claim.Type == "amr")
        {
            return [Destinations.AccessToken, Destinations.IdentityToken];
        }

        if ((claim.Type == Claims.Name || claim.Type == Claims.PreferredUsername) && claim.Subject?.HasScope(Scopes.Profile) == true)
        {
            return [Destinations.AccessToken, Destinations.IdentityToken];
        }

        if ((claim.Type == Claims.Email || claim.Type == "email_verified") && claim.Subject?.HasScope(Scopes.Email) == true)
        {
            return [Destinations.AccessToken, Destinations.IdentityToken];
        }

        if (claim.Type == Claims.Role && claim.Subject?.HasScope(Scopes.Roles) == true)
        {
            return [Destinations.AccessToken, Destinations.IdentityToken];
        }

        return [];
    }
}
