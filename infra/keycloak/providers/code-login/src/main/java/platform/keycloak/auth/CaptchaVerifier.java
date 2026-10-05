package platform.keycloak.auth;

import java.io.IOException;

import com.fasterxml.jackson.databind.JsonNode;

import org.jboss.logging.Logger;
import org.keycloak.http.simple.SimpleHttp;
import org.keycloak.http.simple.SimpleHttpResponse;
import org.keycloak.models.KeycloakSession;

/** Серверная проверка ответа капчи: браузер присылает токен, плагин спрашивает сервис капчи, прошла ли она. */
interface CaptchaVerifier {

    /** Поле формы с токеном — как у виджета Яндекс SmartCaptcha. */
    String FIELD = "smart-token";

    boolean verify(KeycloakSession session, String token, String ip);

    /** Капча не настроена: пройти нельзя — после порога код не уходит (лучше так, чем без защиты). */
    CaptchaVerifier NONE = (session, token, ip) -> false;

    /**
     * Заглушка стенда: проходит токен {@value #STUB_TOKEN}. Включается только явно (captcha-stub=true в настройках
     * плагина) — в бою её нет.
     */
    String STUB_TOKEN = "sandbox-captcha-ok";
    CaptchaVerifier STUB = (session, token, ip) -> STUB_TOKEN.equals(token);

    /** Яндекс SmartCaptcha: POST /validate с серверным ключом, токеном и IP человека. */
    static CaptchaVerifier smartCaptcha(String serverKey, String url) {
        Logger log = Logger.getLogger(CaptchaVerifier.class);
        return (session, token, ip) -> {
            try (SimpleHttpResponse response = SimpleHttp.create(session).doPost(url)
                .param("secret", serverKey)
                .param("token", token)
                .param("ip", ip)
                .asResponse()) {
                if (response.getStatus() != 200) {
                    // Так советует Яндекс: сбой сервиса капчи не должен закрывать вход. Лимиты при этом действуют.
                    log.warnf("SmartCaptcha ответила %d — считаем капчу пройденной", response.getStatus());
                    return true;
                }
                JsonNode json = response.asJson();
                return json != null && "ok".equals(json.path("status").asText());
            } catch (IOException e) {
                log.warnf(e, "SmartCaptcha недоступна — считаем капчу пройденной");
                return true;
            }
        };
    }
}
