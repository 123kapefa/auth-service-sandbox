using AuthService.LegacyImport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

// Перенос пользователей боевого сервиса в Keycloak (этап 6). Запуск:
//   DOTNET_ENVIRONMENT=Development dotnet run --project src/AuthService.LegacyImport [-- --dry-run]
// В бою: ConnectionStrings__Legacy и LegacyImport__Keycloak__ClientSecret — из переменных окружения.
// appsettings*.json — рядом со сборкой: запуск возможен из любой папки.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
LegacyImportOptions options = builder.Configuration.GetSection(LegacyImportOptions.SECTION).Get<LegacyImportOptions>() ?? new();
string connectionString = builder.Configuration.GetConnectionString("Legacy")
    ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Legacy");
if (string.IsNullOrEmpty(options.Keycloak.ClientSecret))
{
    throw new InvalidOperationException("Не задан секрет LegacyImport:Keycloak:ClientSecret");
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<LegacyUserReader>();
builder.Services.AddSingleton(_ => new KeycloakImportClient(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, options.Keycloak));
builder.Services.AddSingleton<LegacyImporter>();

using IHost host = builder.Build();
bool dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
ImportReport report = await host.Services.GetRequiredService<LegacyImporter>().RunAsync(dryRun, CancellationToken.None);
host.Services.GetRequiredService<ILogger<LegacyImporter>>().LogInformation("Готово");
return report.Failed.Count == 0 ? 0 : 1;
