package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Instant;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.Callable;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.stream.IntStream;

import org.junit.jupiter.api.Test;

class CodeLimitStoreTest {

    private static final Instant NOW = Instant.parse("2026-10-07T12:10:00Z");
    private static final LocalDate TODAY = LocalDate.of(2026, 10, 7);
    private static final CodeLimitPolicy POLICY = CodeLimitPolicy.DEFAULTS;

    private final CodeLimitStore store = new CodeLimitStore(new MemorySingleUseObjects(), "realm");

    @Test
    void reserve_concurrentSendsToOneNumber_stopAtHourlyLimit() throws Exception {
        CodeTarget target = CodeTarget.parse("+79001234567").orElseThrow();
        ExecutorService pool = Executors.newFixedThreadPool(16);
        try {
            // Разные IP: держит лимит на номер, а не на IP.
            List<Callable<Boolean>> tasks = IntStream.range(0, 100)
                .<Callable<Boolean>>mapToObj(i -> () -> store.reserve(target, "10.0.0." + i, null, POLICY, NOW) == null)
                .toList();
            long granted = 0;
            for (Future<Boolean> result : pool.invokeAll(tasks)) {
                if (result.get()) {
                    granted++;
                }
            }
            assertEquals(POLICY.targetPerHour(), granted);
        } finally {
            pool.shutdownNow();
        }
    }

    @Test
    void reserve_limitAbove200_stillHolds() {
        CodeLimitPolicy policy = new CodeLimitPolicy(POLICY.cooldowns(), 100, 1000, 500, POLICY.rangePerHour(),
            POLICY.rangeDigits(), POLICY.captchaAfter(), POLICY.countries(), POLICY.dailyBudget(), POLICY.smsCost(),
            POLICY.callCost(), POLICY.phoneChannels(), POLICY.budgetAlertPercent(), POLICY.zone());
        int granted = 0;
        for (int i = 0; i < 600; i++) {
            CodeTarget target = CodeTarget.parse("user" + i + "@example.com").orElseThrow();
            if (store.reserve(target, "10.0.0.1", null, policy, NOW) == null) {
                granted++;
            }
        }
        assertEquals(500, granted);
    }

    @Test
    void reserve_refusal_tellsWhichLimitAndWhenFree() {
        CodeTarget target = CodeTarget.parse("+79001234567").orElseThrow();
        for (int i = 0; i < POLICY.targetPerHour(); i++) {
            assertNull(store.reserve(target, "10.0.0." + i, null, POLICY, NOW));
        }

        CodeLimits.Decision.Limited limited = store.reserve(target, "10.0.0.99", null, POLICY, NOW);

        assertNotNull(limited);
        assertEquals("target-per-hour", limited.limit());
        assertEquals(50 * 60 + 1, limited.retryAfterSeconds());
    }

    @Test
    void reserveSpend_concurrentSms_stayWithinBudget() throws Exception {
        // Бюджет на 10 SMS.
        CodeLimitPolicy policy = new CodeLimitPolicy(POLICY.cooldowns(), POLICY.targetPerHour(), POLICY.targetPerDay(),
            POLICY.ipPerHour(), POLICY.rangePerHour(), POLICY.rangeDigits(), POLICY.captchaAfter(), POLICY.countries(),
            POLICY.smsCost() * 10, POLICY.smsCost(), POLICY.callCost(), POLICY.phoneChannels(), POLICY.budgetAlertPercent(),
            POLICY.zone());
        ExecutorService pool = Executors.newFixedThreadPool(16);
        try {
            List<Callable<Boolean>> tasks = IntStream.range(0, 100)
                .<Callable<Boolean>>mapToObj(i -> () -> store.reserveSpend(CodeDelivery.SMS, policy, TODAY, new ArrayList<>()))
                .toList();
            long granted = 0;
            for (Future<Boolean> result : pool.invokeAll(tasks)) {
                if (result.get()) {
                    granted++;
                }
            }
            assertEquals(10, granted);
            assertEquals(policy.dailyBudget(), store.spent(TODAY, policy));
        } finally {
            pool.shutdownNow();
        }
    }

    @Test
    void release_returnsSpendOfFailedSend() {
        List<Map.Entry<String, Integer>> claimed = new ArrayList<>();
        assertTrue(store.reserveSpend(CodeDelivery.SMS, POLICY, TODAY, claimed));
        assertEquals(POLICY.smsCost(), store.spent(TODAY, POLICY));

        store.release(claimed);

        assertEquals(0, store.spent(TODAY, POLICY));
    }

    @Test
    void claimAlert_onlyOncePerThreshold() {
        assertTrue(store.claimAlert(TODAY, 80));
        assertFalse(store.claimAlert(TODAY, 80));
        assertTrue(store.claimAlert(TODAY, 100));
    }
}
