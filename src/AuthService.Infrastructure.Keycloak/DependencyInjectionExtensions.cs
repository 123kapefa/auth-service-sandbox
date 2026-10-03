using AuthService.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PlatformAuth;

namespace AuthService.Infrastructure.Keycloak;

public static class DependencyInjectionExtensions
{
    /// <summary>
    /// IIdentityProvider (вход) и IIdentityAdmin (Admin API) через Keycloak. Realm — секция Authentication,
    /// клиент — секция Keycloak: тот же клиент ведёт вход и через свою служебную учётку ходит в Admin API.
    /// </summary>
    public static IServiceCollection AddInfrastructureKeycloak(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SECTION_NAME))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<KeycloakOptions>, KeycloakOptionsValidator>();
        services.AddSingleton(TimeProvider.System);

        services.AddHttpClient<IIdentityProvider, KeycloakIdentityProvider>();
        services.AddSingleton<KeycloakServiceAccount>();
        services.AddHttpClient<IIdentityAdmin, KeycloakAdminClient>();
        services.AddHttpClient<IServiceRegistry, KeycloakServiceRegistry>();

        // Discovery кэшируется и сам обновляется; адрес — тот же, по которому сервис берёт ключи для проверки токенов.
        services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(provider =>
        {
            JwtAuthenticationOptions realm = provider.GetRequiredService<IOptions<JwtAuthenticationOptions>>().Value;
            string metadataAddress = string.IsNullOrWhiteSpace(realm.MetadataAddress)
                ? $"{realm.Authority.TrimEnd('/')}/.well-known/openid-configuration"
                : realm.MetadataAddress;

            return new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(provider.GetRequiredService<IHttpClientFactory>().CreateClient())
                {
                    RequireHttps = realm.RequireHttpsMetadata,
                });
        });

        return services;
    }
}
