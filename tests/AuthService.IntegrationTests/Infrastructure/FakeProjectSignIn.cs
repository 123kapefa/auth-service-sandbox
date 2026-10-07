using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Способы входа проекта в памяти вместо плагина Keycloak platform-sign-in. Failure — что вернёт сохранение.</summary>
public sealed class FakeProjectSignIn : IProjectSignIn
{
    public static DateTimeOffset Now { get; } = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    public ProjectSignInSettings Current { get; set; } = Default();

    public int Saves { get; private set; }

    public Error? Failure { get; set; }

    public static ProjectSignInSettings Default() =>
        new(
            ProjectSignInRules.Methods.ToDictionary(
                method => method,
                method => new ProjectSignInMethod(ProjectSignInRules.ON, Configured: method is ProjectSignInRules.YANDEX or ProjectSignInRules.ESIA ? true : null),
                StringComparer.Ordinal),
            TwoFactorRequired: false);

    public void Reset()
    {
        Current = Default();
        Saves = 0;
        Failure = null;
    }

    public Task<Result<ProjectSignInSettings, Error>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<ProjectSignInSettings, Error>(Current));

    // Как плагин: начало переходного периода ставит он сам.
    public Task<Result<ProjectSignInSettings, Error>> SetAsync(ProjectSignInSettings settings, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromResult(Result.Failure<ProjectSignInSettings, Error>(Failure));
        }

        Current = settings with
        {
            Methods = settings.Methods.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Until is null ? pair.Value : pair.Value with { Since = Now },
                StringComparer.Ordinal),
        };
        Saves++;
        return Task.FromResult(Result.Success<ProjectSignInSettings, Error>(Current));
    }

    public Task<Result<SignInMethodUsers, Error>> GetAffectedAsync(string method, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<SignInMethodUsers, Error>(new SignInMethodUsers(method, 8410, 1240)));
}
