package platform.keycloak.auth;

import java.util.List;

import org.keycloak.broker.oidc.OAuth2IdentityProviderConfig;
import org.keycloak.broker.provider.AbstractIdentityProviderFactory;
import org.keycloak.broker.social.SocialIdentityProviderFactory;
import org.keycloak.models.IdentityProviderModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.provider.ProviderConfigurationBuilder;

/** Провайдер «Яндекс ID» (providerId yandex) — в админке Keycloak среди соцсетей. */
public final class YandexIdentityProviderFactory extends AbstractIdentityProviderFactory<YandexIdentityProvider>
    implements SocialIdentityProviderFactory<YandexIdentityProvider> {

    public static final String PROVIDER_ID = "yandex";

    @Override
    public String getName() {
        return "Яндекс ID";
    }

    @Override
    public YandexIdentityProvider create(KeycloakSession session, IdentityProviderModel model) {
        return new YandexIdentityProvider(session, new OAuth2IdentityProviderConfig(model));
    }

    @Override
    public OAuth2IdentityProviderConfig createConfig() {
        return new OAuth2IdentityProviderConfig();
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return ProviderConfigurationBuilder.create()
            .property().name("authorizationUrl").label("Authorization URL")
            .helpText("Пусто — " + YandexIdentityProvider.AUTHORIZATION_URL).type(ProviderConfigProperty.STRING_TYPE).add()
            .property().name("tokenUrl").label("Token URL")
            .helpText("Пусто — " + YandexIdentityProvider.TOKEN_URL).type(ProviderConfigProperty.STRING_TYPE).add()
            .property().name("userInfoUrl").label("User Info URL")
            .helpText("Пусто — " + YandexIdentityProvider.USER_INFO_URL).type(ProviderConfigProperty.STRING_TYPE).add()
            .build();
    }
}
