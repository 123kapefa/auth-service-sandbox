package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;

import io.micrometer.core.instrument.simple.SimpleMeterRegistry;
import org.junit.jupiter.api.Test;

/**
 * Метрики кодов: счётчики проекта создаются нулями сразу, а не с первым событием. Ряд Micrometer, появившийся со значением 1,
 * Prometheus не видит как приращение — первое оповещение о бюджете (раз в сутки) терялось бы.
 */
class CodeMetricsTest {

    private final SimpleMeterRegistry registry = new SimpleMeterRegistry();
    private final CodeMetrics metrics = new CodeMetrics(registry);

    @Test
    void register_CreatesEveryCounterOfTheProjectAtZero() {
        metrics.register("shop");

        for (CodeDelivery channel : CodeDelivery.values()) {
            assertEquals(0, count("platform_code_sent", "realm", "shop", "channel", channel.name()));
            assertEquals(0, count("platform_code_verified", "realm", "shop", "channel", channel.name()));
            assertEquals(0, count("platform_code_cost_kopecks", "realm", "shop", "channel", channel.name()));
        }
        for (String reason : new String[] {"limit", "captcha", "country", "budget", "failed", "method-disabled", "no_phone"}) {
            assertEquals(0, count("platform_code_refused", "realm", "shop", "reason", reason));
        }
        assertEquals(0, count("platform_code_budget_alerts", "realm", "shop", "percent", "80"));
        assertEquals(0, count("platform_code_budget_alerts", "realm", "shop", "percent", "100"));
        assertEquals(0, count("platform_code_captcha_unavailable", "realm", "shop"));
    }

    @Test
    void register_IsIdempotent_AndDoesNotResetCounters() {
        metrics.register("shop");
        metrics.budgetAlert("shop", 80);
        metrics.register("shop");

        assertEquals(1, count("platform_code_budget_alerts", "realm", "shop", "percent", "80"));
    }

    @Test
    void firstEventOfANewProject_RegistersTheRestAtZero() {
        metrics.sent("new-project", CodeDelivery.SMS, 400);

        assertEquals(1, count("platform_code_sent", "realm", "new-project", "channel", "SMS"));
        assertEquals(0, count("platform_code_budget_alerts", "realm", "new-project", "percent", "100"));
        assertEquals(0, count("platform_code_captcha_unavailable", "realm", "new-project"));
        assertEquals(0, count("platform_code_verified", "realm", "new-project", "channel", "SMS"));
    }

    @Test
    void projectsAreSeparate() {
        metrics.register("shop");
        metrics.budgetAlert("other", 100);

        assertEquals(0, count("platform_code_budget_alerts", "realm", "shop", "percent", "100"));
        assertEquals(1, count("platform_code_budget_alerts", "realm", "other", "percent", "100"));
    }

    private double count(String name, String... tags) {
        var counter = registry.find(name).tags(tags).counter();
        assertNotNull(counter, name + " " + String.join(",", tags) + " не создан");
        return counter.count();
    }
}
