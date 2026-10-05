package platform.keycloak.auth;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.JsonNodeFactory;
import com.fasterxml.jackson.databind.node.ObjectNode;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.QueryParam;
import jakarta.ws.rs.core.Response;
import jakarta.ws.rs.core.UriBuilder;

import java.io.IOException;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.UUID;
import java.util.stream.Collectors;

import org.keycloak.OAuth2Constants;
import org.keycloak.broker.oidc.AbstractOAuth2IdentityProvider;
import org.keycloak.broker.provider.AuthenticationRequest;
import org.keycloak.broker.provider.BrokeredIdentityContext;
import org.keycloak.broker.provider.IdentityBrokerException;
import org.keycloak.broker.social.SocialIdentityProvider;
import org.keycloak.events.EventBuilder;
import org.keycloak.http.simple.SimpleHttp;
import org.keycloak.http.simple.SimpleHttpRequest;
import org.keycloak.models.KeycloakContext;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.services.Urls;

/**
 * Вход через Госуслуги (ЕСИА, OAuth 2.0 v2/v3). Отличия от обычного OAuth — в {@link EsiaRequest}: вместо секрета
 * каждый запрос подписывается ({@link EsiaSigner}), в запросе — timestamp и client_certificate_hash, state — UUID.
 * Свой state Keycloak (длинный, с вкладкой и клиентом) хранится на время входа под этим UUID и подставляется обратно,
 * когда ЕСИА возвращает браузер. Данные человека — из REST API ЕСИА по oid из access-токена ({@link EsiaProfile}).
 */
public final class EsiaIdentityProvider extends AbstractOAuth2IdentityProvider<EsiaConfig>
    implements SocialIdentityProvider<EsiaConfig> {

    static final String AUTHORIZATION_URL = EsiaConfig.DEFAULT_BASE + "/aas/oauth2/v2/ac";
    static final String TOKEN_URL = EsiaConfig.DEFAULT_BASE + "/aas/oauth2/v3/te";
    static final String DEFAULT_SCOPE = "openid fullname email mobile";
    private static final String STATE_KEY = "platform-esia-state:";

    public EsiaIdentityProvider(KeycloakSession session, EsiaConfig config) {
        super(session, config);
        if (YandexIdentityProvider.isBlank(config.getAuthorizationUrl())) {
            config.setAuthorizationUrl(AUTHORIZATION_URL);
        }
        if (YandexIdentityProvider.isBlank(config.getTokenUrl())) {
            config.setTokenUrl(TOKEN_URL);
        }
    }

    @Override
    protected String getDefaultScopes() {
        return DEFAULT_SCOPE;
    }

    @Override
    protected UriBuilder createAuthorizationUrl(AuthenticationRequest request) {
        String state = UUID.randomUUID().toString();
        RealmModel realm = request.getRealm();
        session.singleUseObjects().put(STATE_KEY + state, realm.getAccessCodeLifespanLogin(),
            Map.of("state", request.getState().getEncoded()));

        EsiaConfig config = getConfig();
        String scope = config.getDefaultScope();
        String timestamp = EsiaRequest.timestamp(Instant.now());
        String secret = EsiaSigner.sign(session, config,
            EsiaRequest.authorizeText(config.getClientId(), scope, timestamp, state, request.getRedirectUri()));

        Map<String, String> query = new LinkedHashMap<>();
        query.put("client_id", config.getClientId());
        query.put("client_secret", secret);
        query.put("redirect_uri", request.getRedirectUri());
        query.put("scope", scope);
        query.put("response_type", "code");
        query.put("state", state);
        query.put("timestamp", timestamp);
        query.put("access_type", "online");
        query.put("client_certificate_hash", nullToEmpty(config.getCertificateHash()));
        return UriBuilder.fromUri(config.getAuthorizationUrl()).replaceQuery(encode(query));
    }

    @Override
    public Object callback(RealmModel realm, AuthenticationCallback callback, EventBuilder event) {
        return new EsiaEndpoint(callback, realm, event, this);
    }

    @Override
    protected BrokeredIdentityContext doGetFederatedIdentity(String accessToken) {
        String oid;
        try {
            oid = EsiaRequest.oid(accessToken);
        } catch (IllegalArgumentException e) {
            throw new IdentityBrokerException(e.getMessage(), e);
        }
        String person = getConfig().getApiUrl() + "/rs/prns/" + oid;
        JsonNode personJson = api(person, accessToken);
        JsonNode contacts = api(person + "/ctts?embed=(elements)", accessToken);

        ObjectNode raw = JsonNodeFactory.instance.objectNode();
        raw.set("person", personJson);
        raw.set("contacts", contacts);
        return ExternalLogin.context(EsiaProfile.parse(oid, personJson, contacts), raw, getConfig(), this);
    }

    @Override
    public void importNewUser(KeycloakSession session, RealmModel realm, UserModel user, BrokeredIdentityContext context) {
        ExternalLogin.importNewUser(user);
    }

    private JsonNode api(String url, String accessToken) {
        try {
            JsonNode json = SimpleHttp.create(session).doGet(url).auth(accessToken).acceptJson().asJson();
            if (json == null) {
                throw new IdentityBrokerException("Госуслуги: пустой ответ " + url);
            }
            return json;
        } catch (IOException e) {
            throw new IdentityBrokerException("Госуслуги: не удалось получить данные " + url, e);
        }
    }

    /** Свой state Keycloak по UUID, который вернула ЕСИА; один раз — повтор того же ответа не пройдёт. */
    String takeState(String esiaState) {
        if (esiaState == null) {
            return null;
        }
        Map<String, String> saved = session.singleUseObjects().remove(STATE_KEY + esiaState);
        return saved == null ? null : saved.get("state");
    }

    static String encode(Map<String, String> query) {
        return query.entrySet().stream()
            .map(e -> e.getKey() + "=" + URLEncoder.encode(e.getValue(), StandardCharsets.UTF_8))
            .collect(Collectors.joining("&"));
    }

    static String nullToEmpty(String value) {
        return value == null ? "" : value;
    }

    /** Ответ ЕСИА: state — наш UUID → state Keycloak; обмен кода — запрос ЕСИА с подписью. */
    protected static final class EsiaEndpoint extends Endpoint {

        private final EsiaIdentityProvider esia;

        EsiaEndpoint(AuthenticationCallback callback, RealmModel realm, EventBuilder event, EsiaIdentityProvider provider) {
            super(callback, realm, event, provider);
            this.esia = provider;
        }

        @GET
        @Override
        public Response authResponse(@QueryParam(OAUTH2_PARAMETER_STATE) String state,
                                     @QueryParam(OAUTH2_PARAMETER_CODE) String authorizationCode,
                                     @QueryParam(OAuth2Constants.ERROR) String error,
                                     @QueryParam(OAuth2Constants.ERROR_DESCRIPTION) String errorDescription) {
            return super.authResponse(esia.takeState(state), authorizationCode, error, errorDescription);
        }

        @Override
        public SimpleHttpRequest generateTokenRequest(String authorizationCode) {
            KeycloakContext context = session.getContext();
            EsiaConfig config = esia.getConfig();
            String redirectUri = Urls.identityProviderAuthnResponse(context.getUri().getBaseUri(),
                config.getAlias(), context.getRealm().getName()).toString();
            String scope = config.getDefaultScope();
            String state = UUID.randomUUID().toString();
            String timestamp = EsiaRequest.timestamp(Instant.now());
            String secret = EsiaSigner.sign(session, config,
                EsiaRequest.tokenText(config.getClientId(), scope, timestamp, state, redirectUri, authorizationCode));

            return SimpleHttp.create(session).doPost(config.getTokenUrl())
                .param("client_id", config.getClientId())
                .param("code", authorizationCode)
                .param("grant_type", OAUTH2_GRANT_TYPE_AUTHORIZATION_CODE)
                .param("client_secret", secret)
                .param("state", state)
                .param("redirect_uri", redirectUri)
                .param("scope", scope)
                .param("timestamp", timestamp)
                .param("token_type", "Bearer")
                .param("client_certificate_hash", nullToEmpty(config.getCertificateHash()));
        }
    }
}
