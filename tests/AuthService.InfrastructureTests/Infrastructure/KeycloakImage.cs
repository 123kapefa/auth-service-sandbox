using System.Diagnostics;

namespace AuthService.InfrastructureTests.Infrastructure;

/// <summary>
/// Образ Keycloak с нашими плагинами и темой (infra/keycloak/Dockerfile). Готовый образ можно дать переменной
/// KEYCLOAK_TEST_IMAGE (CI собирает его отдельным шагом); иначе тесты собирают его сами через docker build — с кэшем
/// слоёв повторная сборка занимает секунды. Сборка — CLI Docker, а не Testcontainers: Dockerfile использует BuildKit
/// (RUN --mount=type=cache).
/// </summary>
internal static class KeycloakImage
{
    public const string ENVIRONMENT_VARIABLE = "KEYCLOAK_TEST_IMAGE";
    private const string TAG = "auth_service-keycloak:infrastructure-tests";

    public static async Task<string> ResolveAsync()
    {
        string? prebuilt = Environment.GetEnvironmentVariable(ENVIRONMENT_VARIABLE);
        if (!string.IsNullOrWhiteSpace(prebuilt))
        {
            return prebuilt;
        }

        var start = new ProcessStartInfo("docker")
        {
            WorkingDirectory = RepositoryRoot.Directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "build", "--file", "infra/keycloak/Dockerfile", "--tag", TAG, "." })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOCKER_BUILDKIT"] = "1";
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("docker build did not start: is Docker installed?");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"docker build of infra/keycloak/Dockerfile failed ({process.ExitCode}):{Environment.NewLine}{await output}{await error}");
        }

        return TAG;
    }
}

/// <summary>Корень репозитория — там, где AuthService.slnx.</summary>
internal static class RepositoryRoot
{
    private static readonly Lazy<string> s_directory = new(Find);

    public static string Directory => s_directory.Value;

    public static string Path(string relative) => System.IO.Path.Combine(Directory, relative);

    private static string Find()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "AuthService.slnx")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (AuthService.slnx) not found above " + AppContext.BaseDirectory);
    }
}
