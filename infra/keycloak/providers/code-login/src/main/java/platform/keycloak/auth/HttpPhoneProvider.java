package platform.keycloak.auth;

import java.io.IOException;
import java.util.Optional;
import java.util.regex.Pattern;

import com.fasterxml.jackson.databind.JsonNode;

import org.jboss.logging.Logger;
import org.keycloak.http.simple.SimpleHttp;
import org.keycloak.http.simple.SimpleHttpResponse;
import org.keycloak.models.KeycloakSession;

/**
 * Телефония по простому HTTP-протоколу (его понимает заглушка стенда, infra/idp-stub):
 * <ul>
 *   <li>POST {url}/call, phone=79990000001 → 200 {"code":"1234"} — звонок-сброс, code — последние 4 цифры номера,
 *       с которого звонят;</li>
 *   <li>POST {url}/sms, phone, code, text → 200 — SMS.</li>
 * </ul>
 * Любой другой ответ или ошибка сети — «не удалось» (у звонка тогда — запасная SMS).
 */
final class HttpPhoneProvider implements PhoneProvider {

    private static final Logger LOG = Logger.getLogger(HttpPhoneProvider.class);
    private static final Pattern CALL_CODE = Pattern.compile("^\\d{4}$");

    private final String url;

    HttpPhoneProvider(String url) {
        this.url = url.replaceAll("/+$", "");
    }

    @Override
    public Optional<String> call(KeycloakSession session, String phone) {
        try (SimpleHttpResponse response = SimpleHttp.create(session).doPost(url + "/call").param("phone", phone).asResponse()) {
            if (response.getStatus() != 200) {
                LOG.warnf("Звонок на %s не удался: провайдер ответил %d", PhoneNormalizer.toE164(phone), response.getStatus());
                return Optional.empty();
            }
            JsonNode json = response.asJson();
            String code = json == null ? "" : json.path("code").asText();
            return CALL_CODE.matcher(code).matches() ? Optional.of(code) : Optional.empty();
        } catch (IOException e) {
            LOG.warnf(e, "Звонок на %s не удался", PhoneNormalizer.toE164(phone));
            return Optional.empty();
        }
    }

    @Override
    public boolean sms(KeycloakSession session, String phone, String code) {
        try (SimpleHttpResponse response = SimpleHttp.create(session).doPost(url + "/sms")
            .param("phone", phone).param("code", code).param("text", PhoneProvider.smsText(code)).asResponse()) {
            if (response.getStatus() != 200) {
                LOG.warnf("SMS на %s не ушла: провайдер ответил %d", PhoneNormalizer.toE164(phone), response.getStatus());
                return false;
            }
            return true;
        } catch (IOException e) {
            LOG.warnf(e, "SMS на %s не ушла", PhoneNormalizer.toE164(phone));
            return false;
        }
    }
}
