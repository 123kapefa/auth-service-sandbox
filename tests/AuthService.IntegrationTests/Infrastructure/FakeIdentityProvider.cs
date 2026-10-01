using System.Collections.ObjectModel;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Подставной сервер входа: отвечает так, как задал тест, и запоминает, что у него спрашивали.</summary>
public sealed class FakeIdentityProvider : IIdentityProvider
{
    public static readonly DateTimeOffset AccessExpires = DateTimeOffset.UtcNow.AddMinutes(15);

    public AuthorizationRequest? LastAuthorization { get; private set; }

    public Collection<(string Code, string CodeVerifier, Uri RedirectUri)> Exchanges { get; } = [];

    public Collection<string> Refreshes { get; } = [];

    public Collection<string> Logouts { get; } = [];

    public Result<TokenSet, Error> TokenResult { get; set; } = Tokens("access-1", "refresh-1");

    public UnitResult<Error> LogoutResult { get; set; } = UnitResult.Success<Error>();

    public static TokenSet Tokens(string access, string refresh) =>
        new(access, AccessExpires, refresh, DateTimeOffset.UtcNow.AddDays(30));

    public void Reset()
    {
        LastAuthorization = null;
        Exchanges.Clear();
        Refreshes.Clear();
        Logouts.Clear();
        TokenResult = Tokens("access-1", "refresh-1");
        LogoutResult = UnitResult.Success<Error>();
    }

    public Task<Result<AuthorizationRequest, Error>> CreateAuthorizationRequestAsync(
        Uri redirectUri,
        bool forceLogin,
        CancellationToken cancellationToken)
    {
        string state = Guid.NewGuid().ToString("N");
        string prompt = forceLogin ? "&prompt=login" : string.Empty;
        LastAuthorization = new AuthorizationRequest(
            new Uri($"http://keycloak.test/auth?state={state}&redirect_uri={Uri.EscapeDataString(redirectUri.ToString())}{prompt}"),
            state,
            $"verifier-{state}");
        return Task.FromResult(Result.Success<AuthorizationRequest, Error>(LastAuthorization));
    }

    public Task<Result<TokenSet, Error>> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken)
    {
        Exchanges.Add((code, codeVerifier, redirectUri));
        return Task.FromResult(TokenResult);
    }

    public Task<Result<TokenSet, Error>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        Refreshes.Add(refreshToken);
        return Task.FromResult(TokenResult);
    }

    public Task<UnitResult<Error>> LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        Logouts.Add(refreshToken);
        return Task.FromResult(LogoutResult);
    }
}
