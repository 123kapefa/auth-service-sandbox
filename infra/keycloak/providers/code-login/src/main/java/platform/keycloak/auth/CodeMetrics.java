package platform.keycloak.auth;

import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import io.micrometer.core.instrument.Metrics;

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
 * Оповещение о падении доли введённых — правило Prometheus (docs/keycloak/10-code-limits.md).
 */
final class CodeMetrics {

    private final MeterRegistry registry;

    CodeMetrics(MeterRegistry registry) {
        this.registry = registry;
    }

    /** Общий реестр Micrometer: Keycloak (Quarkus) добавляет в него свой реестр Prometheus. */
    static CodeMetrics global() {
        return new CodeMetrics(Metrics.globalRegistry);
    }

    void sent(String realm, CodeDelivery channel, long cost) {
        counter("platform_code_sent", realm, "channel", channel.name()).increment();
        if (cost > 0) {
            counter("platform_code_cost_kopecks", realm, "channel", channel.name()).increment(cost);
        }
    }

    void verified(String realm, CodeDelivery channel) {
        counter("platform_code_verified", realm, "channel", channel.name()).increment();
    }

    void refused(String realm, String reason) {
        counter("platform_code_refused", realm, "reason", reason).increment();
    }

    void budgetAlert(String realm, int percent) {
        counter("platform_code_budget_alerts", realm, "percent", Integer.toString(percent)).increment();
    }

    void captchaUnavailable(String realm) {
        Counter.builder("platform_code_captcha_unavailable").tag("realm", realm).register(registry).increment();
    }

    private Counter counter(String name, String realm, String tag, String value) {
        return Counter.builder(name).tag("realm", realm).tag(tag, value).register(registry);
    }
}
