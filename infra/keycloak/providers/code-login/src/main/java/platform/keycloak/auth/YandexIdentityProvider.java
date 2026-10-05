package platform.keycloak.auth;

import com.fasterxml.jackson.databind.JsonNode;

import java.io.IOException;

import org.keycloak.broker.oidc.AbstractOAuth2IdentityProvider;
import org.keycloak.broker.oidc.OAuth2IdentityProviderConfig;
import org.keycloak.broker.provider.BrokeredIdentityContext;
import org.keycloak.broker.provider.IdentityBrokerException;
import org.keycloak.broker.social.SocialIdentityProvider;
import org.keycloak.http.simple.SimpleHttp;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;

/**
 * Вход через Яндекс ID (OAuth 2.0, https://yandex.ru/dev/id/doc/ru/). Код → токен (client_secret в теле запроса) →
 * профиль login.yandex.ru/info с заголовком «Authorization: OAuth &lt;токен&gt;». Адреса можно переопределить в
 * настройках провайдера (authorizationUrl, tokenUrl, userInfoUrl) — так песочница ходит в заглушку.
 */
public final class YandexIdentityProvider extends AbstractOAuth2IdentityProvider<OAuth2IdentityProviderConfig>
    implements SocialIdentityProvider<OAuth2IdentityProviderConfig> {

    static final String AUTHORIZATION_URL = "https://oauth.yandex.ru/authorize";
    static final String TOKEN_URL = "https://oauth.yandex.ru/token";
    static final String USER_INFO_URL = "https://login.yandex.ru/info?format=json";
    static final String DEFAULT_SCOPE = "login:info login:email login:default_phone";

    public YandexIdentityProvider(KeycloakSession session, OAuth2IdentityProviderConfig config) {
        super(session, config);
        if (isBlank(config.getAuthorizationUrl())) {
            config.setAuthorizationUrl(AUTHORIZATION_URL);
        }
        if (isBlank(config.getTokenUrl())) {
            config.setTokenUrl(TOKEN_URL);
        }
        if (isBlank(config.getUserInfoUrl())) {
            config.setUserInfoUrl(USER_INFO_URL);
        }
    }

    @Override
    protected String getDefaultScopes() {
        return DEFAULT_SCOPE;
    }

    @Override
    protected BrokeredIdentityContext doGetFederatedIdentity(String accessToken) {
        JsonNode profile;
        try {
            profile = SimpleHttp.create(session).doGet(getConfig().getUserInfoUrl())
                .header("Authorization", "OAuth " + accessToken)
                .asJson();
        } catch (IOException e) {
            throw new IdentityBrokerException("Яндекс ID: не удалось получить профиль", e);
        }
        if (profile == null || profile.path("id").isMissingNode()) {
            throw new IdentityBrokerException("Яндекс ID: ответ без профиля");
        }
        return ExternalLogin.context(YandexProfile.parse(profile), profile, getConfig(), this);
    }

    @Override
    public void importNewUser(KeycloakSession session, RealmModel realm, UserModel user, BrokeredIdentityContext context) {
        ExternalLogin.importNewUser(user);
    }

    static boolean isBlank(String value) {
        return value == null || value.isBlank();
    }
}
