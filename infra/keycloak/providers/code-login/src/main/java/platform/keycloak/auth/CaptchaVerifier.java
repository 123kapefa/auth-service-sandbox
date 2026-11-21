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
    /** Ключ сайта у заглушки: тема показывает галочку «Я не робот» стенда, которая ставит STUB_TOKEN. */
    String STUB_SITE_KEY = "sandbox-stub";
    CaptchaVerifier STUB = (session, token, ip) -> STUB_TOKEN.equals(token);

    /** Чем кончилась серверная проверка. */
    enum Answer {
        /** Капча пройдена. */
        PASSED,
        /** Не пройдена, или сервис ответил так, что пройденной её не считать (4xx, непонятный ответ). */
        FAILED,
        /** Сервис капчи недоступен (5xx, таймаут, обрыв): вход не закрываем, лимиты действуют. */
        UNAVAILABLE
    }

    /**
     * Что делать, когда сервис капчи недоступен (5xx, таймаут, обрыв). PASS (по умолчанию) — вход не закрываем, лимиты
     * действуют: так советует Яндекс, сбой капчи не должен класть вход. REJECT — капча не пройдена, пока сервис недоступен:
     * строже, но на время сбоя код не уходит там, где лимиты требуют капчу.
     */
    enum OnOutage {
        PASS, REJECT;

        /** Значение настройки captcha-on-outage; пусто — PASS; опечатка — ошибка (Keycloak не стартует). */
        static OnOutage parse(String value) {
            if (value == null || value.isBlank()) {
                return PASS;
            }
            return switch (value.trim().toLowerCase(java.util.Locale.ROOT)) {
                case "pass" -> PASS;
                case "reject" -> REJECT;
                default -> throw new IllegalArgumentException("captcha-on-outage: «" + value + "» — нужно pass или reject");
            };
        }
    }

    /** Итог проверки → пройдена ли капча с учётом режима при сбое сервиса. */
    static boolean passes(Answer answer, OnOutage onOutage) {
        return switch (answer) {
            case PASSED -> true;
            case FAILED -> false;
            case UNAVAILABLE -> onOutage == OnOutage.PASS;
        };
    }

    /**
     * Ответ SmartCaptcha → итог. Пропускаем только при сбое самого сервиса (5xx): так советует Яндекс. 4xx — ошибка
     * нашей стороны (неверный серверный ключ, адрес), и пропускать на ней любой токен нельзя: капча перестала бы
     * защищать, а сигнал был бы только в WARN. Непонятное тело на 200 — тоже отказ.
     */
    static Answer answer(int status, JsonNode json) {
        if (status >= 500) {
            return Answer.UNAVAILABLE;
        }
        if (status != 200 || json == null) {
            return Answer.FAILED;
        }
        return "ok".equals(json.path("status").asText()) ? Answer.PASSED : Answer.FAILED;
    }

    /**
     * Яндекс SmartCaptcha: POST /validate с серверным ключом, токеном и IP человека. Сервис недоступен — по настройке
     * captcha-on-outage: pass (по умолчанию) считает капчу пройденной, reject — нет (WARN и метрика
     * platform_code_captcha_unavailable_total в обоих случаях).
     */
    static CaptchaVerifier smartCaptcha(String serverKey, String url, CodeMetrics metrics, OnOutage onOutage) {
        Logger log = Logger.getLogger(CaptchaVerifier.class);
        return (session, token, ip) -> {
            Answer answer;
            try (SimpleHttpResponse response = SimpleHttp.create(session).doPost(url)
                .param("secret", serverKey)
                .param("token", token)
                .param("ip", ip)
                .asResponse()) {
                JsonNode json = null;
                if (response.getStatus() == 200) {
                    try {
                        json = response.asJson();
                    } catch (IOException | RuntimeException e) {
                        log.warnf("SmartCaptcha: непонятный ответ — капча не пройдена (%s)", e.getMessage());
                    }
                }
                answer = answer(response.getStatus(), json);
                if (answer != Answer.PASSED && response.getStatus() != 200) {
                    log.warnf("SmartCaptcha ответила %d — %s", response.getStatus(),
                        answer == Answer.UNAVAILABLE ? outageText(onOutage) : "капча не пройдена (проверьте ключ и адрес)");
                }
            } catch (IOException e) {
                log.warnf(e, "SmartCaptcha недоступна — %s", outageText(onOutage));
                answer = Answer.UNAVAILABLE;
            }
            if (answer == Answer.UNAVAILABLE && metrics != null && session.getContext().getRealm() != null) {
                metrics.captchaUnavailable(session.getContext().getRealm().getName());
            }
            return passes(answer, onOutage);
        };
    }

    private static String outageText(OnOutage onOutage) {
        return onOutage == OnOutage.PASS ? "считаем капчу пройденной" : "капча не пройдена (captcha-on-outage=reject)";
    }
}
