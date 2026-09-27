using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Features.Auth.UseCases.Refresh;

/// <summary>Новые токены по refresh-токену; старый после этого недействителен (ротация на стороне Keycloak).</summary>
public sealed class RefreshHandler(IIdentityProvider identityProvider)
{
    public async Task<Result<TokenSet, Error>> Handle(string? refreshToken, CancellationToken cancellationToken) =>
        refreshToken is null
            ? AuthErrors.SessionMissing()
            : await identityProvider.RefreshAsync(refreshToken, cancellationToken);
}
