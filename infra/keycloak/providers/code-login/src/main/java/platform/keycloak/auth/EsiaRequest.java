package platform.keycloak.auth;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import java.io.IOException;
import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.Base64;

/**
 * Протокол Госуслуг (ЕСИА, OAuth 2.0 по методическим рекомендациям ЕСИА): что подписывается и как. Без Keycloak —
 * проверяется юнит-тестами.
 * <ul>
 *   <li>Запрос кода (/aas/oauth2/v2/ac): client_secret — подпись строки client_id + scope + timestamp + state + redirect_uri.</li>
 *   <li>Обмен кода на токен (/aas/oauth2/v3/te): подпись client_id + scope + timestamp + state + redirect_uri + code.</li>
 * </ul>
 * Подпись — открепленная (PKCS#7) по ГОСТ Р 34.10-2012 ключом сертификата, зарегистрированного в ЕСИА; делает её
 * сервис подписи ({@link EsiaSigner}), в запрос она идёт в base64 url-safe. state — UUID, timestamp —
 * «yyyy.MM.dd HH:mm:ss Z».
 */
public final class EsiaRequest {

    private static final DateTimeFormatter TIMESTAMP = DateTimeFormatter.ofPattern("yyyy.MM.dd HH:mm:ss Z").withZone(ZoneId.of("Europe/Moscow"));
    private static final ObjectMapper JSON = new ObjectMapper();
    /** Claim access-токена ЕСИА с oid — номером учётной записи человека. */
    static final String OID_CLAIM = "urn:esia:sbj_id";

    private EsiaRequest() {
    }

    public static String timestamp(Instant now) {
        return TIMESTAMP.format(now);
    }

    public static String authorizeText(String clientId, String scope, String timestamp, String state, String redirectUri) {
        return clientId + scope + timestamp + state + redirectUri;
    }

    public static String tokenText(String clientId, String scope, String timestamp, String state, String redirectUri, String code) {
        return authorizeText(clientId, scope, timestamp, state, redirectUri) + code;
    }

    /** Подпись от сервиса подписи (base64) → вид для запроса ЕСИА: base64 url-safe без «=». */
    public static String urlSafe(String base64Signature) {
        byte[] raw = Base64.getMimeDecoder().decode(base64Signature.trim());
        return Base64.getUrlEncoder().withoutPadding().encodeToString(raw);
    }

    /**
     * oid человека из access-токена ЕСИА (JWT, claim urn:esia:sbj_id). Подпись токена не проверяется: токен получен
     * Keycloak напрямую от ЕСИА в ответ на код (TLS), а не от браузера.
     */
    public static String oid(String accessToken) {
        String[] parts = accessToken.split("\\.");
        if (parts.length < 2) {
            throw new IllegalArgumentException("Госуслуги: access-токен — не JWT");
        }
        try {
            JsonNode payload = JSON.readTree(Base64.getUrlDecoder().decode(parts[1]));
            String oid = YandexProfile.text(payload, OID_CLAIM);
            if (oid == null) {
                throw new IllegalArgumentException("Госуслуги: в access-токене нет " + OID_CLAIM);
            }
            return oid;
        } catch (IOException e) {
            throw new IllegalArgumentException("Госуслуги: access-токен не читается", e);
        }
    }
}
