package platform.keycloak.auth;

import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.core.instrument.Metrics;

import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Метрики кодов (Prometheus Keycloak, /metrics на порту управления при metrics-enabled):
 * <ul>
 *   <li>platform_code_sent_total{realm,channel} — отправлено кодов (CALL, SMS, EMAIL);</li>
 *   <li>platform_code_verified_total{realm,channel} — введено верно (доля введённых = verified / sent);</li>
 *   <li>platform_code_cost_kopecks_total{realm,channel} — расход;</li>
 *   <li>platform_code_refused_total{realm,reason} — не отправлено: limit, captcha, country, budget,
 *       failed (провайдер не смог), method-disabled;</li>
 *   <li>platform_code_budget_alerts_total{realm,percent} — оповещения о расходе (80, 100);</li>
 *   <li>platform_code_captcha_unavailable_total{realm} — сервис капчи недоступен, капча считалась пройденной.</li>
 * </ul>
 * Оповещение о падении доли введённых — правило Prometheus.
 */
final class CodeMetrics {

    /** Причины отказа в отправке кода (platform_code_refused_total{reason}). */
    private static final String[] REFUSED_REASONS = {"limit", "captcha", "country", "budget", "failed", "method-disabled", "no_phone"};

    private final MeterRegistry registry;
    private final Set<String> registered = ConcurrentHashMap.newKeySet();

    CodeMetrics(MeterRegistry registry) {
        this.registry = registry;
    }

    /** Общий реестр Micrometer: Keycloak (Quarkus) добавляет в него свой реестр Prometheus. */
    static CodeMetrics global() {
        return new CodeMetrics(Metrics.globalRegistry);
    }

    /**
     * Создаёт счётчики проекта нулями — сразу все. Счётчик Micrometer появляется в выдаче только с первым событием и сразу со значением 1,
     * а Prometheus не видит приращения у ряда без предыдущей точки: первое оповещение о бюджете (оно приходит раз в сутки), первая недоступность
     * капчи и первый код терялись бы в increase(). Нулевые ряды попадают в выдачу заранее — при старте Keycloak для существующих проектов
     * и при первом событии нового проекта, задолго до бюджета и капчи. Повторный вызов ничего не меняет.
     */
    void register(String realm) {
        if (!registered.add(realm)) {
            return;
        }
        for (CodeDelivery channel : CodeDelivery.values()) {
            counter("platform_code_sent", realm, "channel", channel.name());
            counter("platform_code_verified", realm, "channel", channel.name());
            counter("platform_code_cost_kopecks", realm, "channel", channel.name());
        }
        for (String reason : REFUSED_REASONS) {
            counter("platform_code_refused", realm, "reason", reason);
        }
        counter("platform_code_budget_alerts", realm, "percent", "80");
        counter("platform_code_budget_alerts", realm, "percent", "100");
        Counter.builder("platform_code_captcha_unavailable").tag("realm", realm).register(registry);
    }

    void sent(String realm, CodeDelivery channel, long cost) {
        register(realm);
        counter("platform_code_sent", realm, "channel", channel.name()).increment();
        if (cost > 0) {
            counter("platform_code_cost_kopecks", realm, "channel", channel.name()).increment(cost);
        }
    }

    void verified(String realm, CodeDelivery channel) {
        register(realm);
        counter("platform_code_verified", realm, "channel", channel.name()).increment();
    }

    void refused(String realm, String reason) {
        register(realm);
        counter("platform_code_refused", realm, "reason", reason).increment();
    }

    void budgetAlert(String realm, int percent) {
        register(realm);
        counter("platform_code_budget_alerts", realm, "percent", Integer.toString(percent)).increment();
    }

    void captchaUnavailable(String realm) {
        register(realm);
        Counter.builder("platform_code_captcha_unavailable").tag("realm", realm).register(registry).increment();
    }

    private Counter counter(String name, String realm, String tag, String value) {
        return Counter.builder(name).tag("realm", realm).tag(tag, value).register(registry);
    }
}
