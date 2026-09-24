namespace AuthService.Contracts;

public sealed record CreateClientRequest(
    string ClientId,
    string DisplayName,
    OAuthClientKind Kind,
    IReadOnlyCollection<string> RedirectUris,
    IReadOnlyCollection<string> PostLogoutRedirectUris,
    IReadOnlyCollection<string> AllowedScopes,
    bool Trusted = true);

public enum OAuthClientKind
{
    Public,
    Confidential,
    Service
}

public sealed record ClientResponse(
    string ClientId,
    string? DisplayName,
    OAuthClientKind Kind,
    IReadOnlyCollection<string> RedirectUris,
    IReadOnlyCollection<string> AllowedScopes);

public sealed record ClientSecretResponse(string ClientId, string Secret);

public sealed record CreateScopeRequest(
    string Name,
    string DisplayName,
    IReadOnlyCollection<string> Resources);

public sealed record ScopeResponse(
    string Name,
    string? DisplayName,
    IReadOnlyCollection<string> Resources);

public sealed record SetPlatformRoleRequest(bool IsPlatformAdmin);
