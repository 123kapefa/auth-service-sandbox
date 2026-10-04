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

    /// <summary>Своё у приложений (clientId → оформление); нет записи — как у проекта.</summary>
    public Dictionary<string, AppBranding> Apps { get; } = new(StringComparer.Ordinal);

    /// <summary>Каким приложением спросили итоговое оформление в последний раз.</summary>
    public string? LastEffectiveApp { get; private set; }

    public void Reset()
    {
        Current = Default;
        Saves = 0;
        Failure = null;
        Apps.Clear();
        LastEffectiveApp = null;
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

    public Task<Result<AppBranding, Error>> GetAppAsync(string clientId, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<AppBranding, Error>(Apps.GetValueOrDefault(clientId, AppBranding.AsProject)));

    public Task<UnitResult<Error>> SetAppAsync(string clientId, AppBranding branding, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromResult(UnitResult.Failure(Failure));
        }

        Apps[clientId] = branding;
        Saves++;
        return Task.FromResult(UnitResult.Success<Error>());
    }

    public Task<Result<ProjectBranding, Error>> GetEffectiveAsync(string? app, CancellationToken cancellationToken)
    {
        LastEffectiveApp = app;
        ProjectBranding effective = Current;
        if (app is not null && Apps.TryGetValue(app, out AppBranding? own))
        {
            effective = effective with { Name = own.Name ?? effective.Name, Logo = own.Logo ?? effective.Logo };
            if (own.Color is not null)
            {
                // Акцент приложения — парой, как в плагине: без своего тёмного — тот же цвет и в тёмной теме.
                effective = effective with { Color = own.Color, ColorDark = own.ColorDark };
            }
        }

        return Task.FromResult(Result.Success<ProjectBranding, Error>(effective));
    }
}
