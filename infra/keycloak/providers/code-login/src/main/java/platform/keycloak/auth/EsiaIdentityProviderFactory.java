package platform.keycloak.auth;

import java.util.List;

import org.keycloak.broker.provider.AbstractIdentityProviderFactory;
import org.keycloak.broker.social.SocialIdentityProviderFactory;
import org.keycloak.models.IdentityProviderModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.provider.ProviderConfigurationBuilder;

/** Провайдер «Госуслуги» (providerId esia) — в админке Keycloak среди соцсетей. */
public final class EsiaIdentityProviderFactory extends AbstractIdentityProviderFactory<EsiaIdentityProvider>
    implements SocialIdentityProviderFactory<EsiaIdentityProvider> {

    public static final String PROVIDER_ID = "esia";

    @Override
    public String getName() {
        return "Госуслуги";
    }

    @Override
    public EsiaIdentityProvider create(KeycloakSession session, IdentityProviderModel model) {
        return new EsiaIdentityProvider(session, new EsiaConfig(model));
    }

    @Override
    public EsiaConfig createConfig() {
        return new EsiaConfig();
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return ProviderConfigurationBuilder.create()
            .property().name(EsiaConfig.SIGNER_URL).label("Сервис подписи")
            .helpText("POST текста → открепленная подпись ГОСТ в base64").type(ProviderConfigProperty.STRING_TYPE).add()
            .property().name(EsiaConfig.CERTIFICATE_HASH).label("Хэш сертификата (client_certificate_hash)")
            .type(ProviderConfigProperty.STRING_TYPE).add()
            .property().name(EsiaConfig.API_URL).label("API ЕСИА")
            .helpText("Пусто — " + EsiaConfig.DEFAULT_BASE + "; тестовая среда — https://esia-portal1.test.gosuslugi.ru")
            .type(ProviderConfigProperty.STRING_TYPE).add()
            .property().name("authorizationUrl").label("Authorization URL")
            .helpText("Пусто — " + EsiaIdentityProvider.AUTHORIZATION_URL).type(ProviderConfigProperty.STRING_TYPE).add()
            .property().name("tokenUrl").label("Token URL")
            .helpText("Пусто — " + EsiaIdentityProvider.TOKEN_URL).type(ProviderConfigProperty.STRING_TYPE).add()
            .build();
    }
}
