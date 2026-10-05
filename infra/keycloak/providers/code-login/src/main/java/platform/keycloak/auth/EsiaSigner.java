package platform.keycloak.auth;

import java.io.IOException;
import java.nio.charset.StandardCharsets;

import org.apache.http.entity.ContentType;
import org.apache.http.entity.StringEntity;
import org.keycloak.broker.provider.IdentityBrokerException;
import org.keycloak.http.simple.SimpleHttp;
import org.keycloak.http.simple.SimpleHttpResponse;
import org.keycloak.models.KeycloakSession;

/**
 * Подпись запросов к ЕСИА — внешним сервисом подписи. Подписать по ГОСТ Р 34.10-2012 в JVM Keycloak без
 * сертифицированной криптографии нельзя, а ключ системы лучше держать отдельно от Keycloak. Контракт простой:
 * POST {signerUrl}, тело — текст (UTF-8), ответ 200 — открепленная подпись PKCS#7 в base64.
 * В песочнице подписывает заглушка (infra/idp-stub).
 */
final class EsiaSigner {

    private EsiaSigner() {
    }

    static String sign(KeycloakSession session, EsiaConfig config, String text) {
        if (YandexIdentityProvider.isBlank(config.getSignerUrl())) {
            throw new IdentityBrokerException("Госуслуги: не задан адрес сервиса подписи (signerUrl)");
        }
        try (SimpleHttpResponse response = SimpleHttp.create(session).doPost(config.getSignerUrl())
            .entity(new StringEntity(text, ContentType.create("text/plain", StandardCharsets.UTF_8)))
            .asResponse()) {
            if (response.getStatus() != 200) {
                throw new IdentityBrokerException("Госуслуги: сервис подписи ответил " + response.getStatus());
            }
            return EsiaRequest.urlSafe(response.asString());
        } catch (IOException e) {
            throw new IdentityBrokerException("Госуслуги: сервис подписи недоступен", e);
        }
    }
}
