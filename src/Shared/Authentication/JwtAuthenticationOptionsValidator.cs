using Microsoft.Extensions.Options;

namespace PlatformAuth;

/// <summary>Проверка секции Authentication при старте: с кривыми настройками сервис не запускается.</summary>
public sealed class JwtAuthenticationOptionsValidator : IValidateOptions<JwtAuthenticationOptions>
{
    private const string SECTION = JwtAuthenticationOptions.SECTION_NAME;

    public ValidateOptionsResult Validate(string? name, JwtAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        CheckUrl(failures, $"{SECTION}:Authority", options.Authority, isRequired: true, options.RequireHttpsMetadata);
        CheckUrl(failures, $"{SECTION}:MetadataAddress", options.MetadataAddress, isRequired: false, options.RequireHttpsMetadata);
        CheckUrl(failures, $"{SECTION}:ValidIssuer", options.ValidIssuer, isRequired: false, requireHttps: false);

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{SECTION}:Audience is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckUrl(List<string> failures, string key, string? value, bool isRequired, bool requireHttps)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (isRequired)
            {
                failures.Add($"{key} is required.");
            }

            return;
        }

        // «keycloak:8180/realms» — тоже абсолютный Uri (схема keycloak:), поэтому схему проверяем явно.
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                 || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            failures.Add($"{key} must be an absolute URL: «{value}».");
            return;
        }

        if (requireHttps && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            failures.Add($"{key} must use https when {SECTION}:RequireHttpsMetadata is true: «{value}».");
        }
    }
}
