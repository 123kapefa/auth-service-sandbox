using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Оформление проекта в памяти вместо плагина Keycloak platform-branding. Failure — что вернёт сохранение.</summary>
public sealed class FakeProjectBranding : IProjectBranding
{
    public static ProjectBranding Default { get; } = new("Auth Sandbox", null, null, BrandingRules.THEME_SYSTEM);

    public ProjectBranding Current { get; set; } = Default;

    public int Saves { get; private set; }

    public Error? Failure { get; set; }

    public void Reset()
    {
        Current = Default;
        Saves = 0;
        Failure = null;
    }

    public Task<Result<ProjectBranding, Error>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<ProjectBranding, Error>(Current));

    public Task<UnitResult<Error>> SetAsync(ProjectBranding branding, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromResult(UnitResult.Failure(Failure));
        }

        Current = branding;
        Saves++;
        return Task.FromResult(UnitResult.Success<Error>());
    }
}
