using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Настройки кодов проекта в памяти вместо плагина Keycloak platform-codes. Failure — что вернёт сохранение.</summary>
public sealed class FakeProjectCodes : IProjectCodes
{
    public static ProjectCodeSettings Default { get; } = new(["call", "sms"], ["7"], 100_000, 5, 10, 10, 20, 2);

    public static ProjectCodeServerSettings Server { get; } = new(7, 400, 100, 80, "Europe/Moscow");

    public ProjectCodeSettings Current { get; set; } = Default;

    public ProjectCodeUsage Usage { get; set; } = Empty;

    public int Saves { get; private set; }

    public Error? Failure { get; set; }

    private static ProjectCodeUsage Empty { get; } =
        new(new DateOnly(2026, 10, 7), "Europe/Moscow", 0, 100_000, 80, Exhausted: false, new(0, 0), new(0, 0), new(0, 0), 0, 0, null);

    public void Reset()
    {
        Current = Default;
        Usage = Empty;
        Saves = 0;
        Failure = null;
    }

    public Task<Result<ProjectCodes, Error>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<ProjectCodes, Error>(new ProjectCodes(Current, Server)));

    public Task<Result<ProjectCodes, Error>> SetAsync(ProjectCodeSettings settings, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromResult(Result.Failure<ProjectCodes, Error>(Failure));
        }

        Current = settings;
        Saves++;
        return Task.FromResult(Result.Success<ProjectCodes, Error>(new ProjectCodes(Current, Server)));
    }

    public Task<Result<ProjectCodeUsage, Error>> GetUsageAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<ProjectCodeUsage, Error>(Usage));
}
