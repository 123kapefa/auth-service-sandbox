package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.util.List;
import java.util.Map;

import org.junit.jupiter.api.Test;

class CodeLimitsTest {

    private static final Instant NOW = Instant.parse("2026-10-05T12:00:00Z");
    private static final CodeTarget PHONE = new CodeTarget(CodeTarget.Channel.SMS, "79990000001");
    private static final CodeTarget EMAIL = new CodeTarget(CodeTarget.Channel.EMAIL, "a@sandbox.test");

    private final CodeLimits limits = new CodeLimits(CodeLimitPolicy.DEFAULTS);

    @Test
    void cooldown_grows_60s_2m_5m_15m_andStays() {
        SendHistory history = SendHistory.EMPTY;
        List<Long> cooldowns = new java.util.ArrayList<>();
        for (int i = 0; i < 5; i++) {
            cooldowns.add(limits.cooldown(history, NOW).toSeconds());
            history = history.plus(NOW.minusSeconds(60 - i), CodeLimits.DAY);
        }
        assertEquals(List.of(60L, 120L, 300L, 900L, 900L), cooldowns);
    }

    @Test
    void cooldown_resetsWhenLastHourIsQuiet() {
        SendHistory history = sends(3, NOW.minus(Duration.ofMinutes(63)));
        assertEquals(60, limits.cooldown(history, NOW).toSeconds());
    }

    @Test
    void firstSends_allowed_withoutCaptcha() {
        CodeLimits.Decision decision = limits.check(PHONE, CodeDelivery.SMS, usage(sends(1, NOW.minusSeconds(120)), SendHistory.EMPTY), false, NOW);
        CodeLimits.Decision.Allow allow = assertInstanceOf(CodeLimits.Decision.Allow.class, decision);
        assertEquals(400, allow.cost());
    }

    @Test
    void captcha_afterTwoSendsInHour_toNumberOrFromIp() {
        assertInstanceOf(CodeLimits.Decision.CaptchaRequired.class,
            limits.check(PHONE, CodeDelivery.SMS, usage(sends(2, NOW.minusSeconds(600)), SendHistory.EMPTY), false, NOW));
        assertInstanceOf(CodeLimits.Decision.CaptchaRequired.class,
            limits.check(PHONE, CodeDelivery.SMS, usage(SendHistory.EMPTY, sends(2, NOW.minusSeconds(600))), false, NOW));
        assertInstanceOf(CodeLimits.Decision.Allow.class,
            limits.check(PHONE, CodeDelivery.SMS, usage(sends(2, NOW.minusSeconds(600)), SendHistory.EMPTY), true, NOW));
    }

    @Test
    void numberLimit_5PerHour_refusedEvenWithCaptcha() {
        Instant first = NOW.minus(Duration.ofMinutes(50));
        CodeLimits.Decision decision = limits.check(PHONE, CodeDelivery.SMS, usage(sends(5, first), SendHistory.EMPTY), true, NOW);

        CodeLimits.Decision.Limited limited = assertInstanceOf(CodeLimits.Decision.Limited.class, decision);
        assertEquals("target-per-hour", limited.limit());
        // Место освободится, когда из часа выйдет первая из пяти отправок: через 10 минут.
        assertEquals(600, limited.retryAfterSeconds());
    }

    @Test
    void numberLimit_10PerDay() {
        SendHistory history = sends(10, NOW.minus(Duration.ofHours(20)));
        CodeLimits.Decision.Limited limited = assertInstanceOf(CodeLimits.Decision.Limited.class,
            limits.check(PHONE, CodeDelivery.SMS, usage(history, SendHistory.EMPTY), true, NOW));
        assertEquals("target-per-day", limited.limit());
        assertEquals(Duration.ofHours(4).toSeconds(), limited.retryAfterSeconds());
    }

    @Test
    void ipLimit_10PerHour() {
        CodeLimits.Decision.Limited limited = assertInstanceOf(CodeLimits.Decision.Limited.class,
            limits.check(PHONE, CodeDelivery.SMS, usage(SendHistory.EMPTY, sends(10, NOW.minusSeconds(1200))), true, NOW));
        assertEquals("ip-per-hour", limited.limit());
    }

    @Test
    void rangeLimit_20PerHour_byFirst7Digits() {
        assertEquals("7999000", limits.range(PHONE));
        assertNull(limits.range(EMAIL));

        CodeLimits.Usage usage = new CodeLimits.Usage(SendHistory.EMPTY, SendHistory.EMPTY, sends(20, NOW.minusSeconds(1200)), 0);
        CodeLimits.Decision.Limited limited = assertInstanceOf(CodeLimits.Decision.Limited.class, limits.check(PHONE, CodeDelivery.SMS, usage, true, NOW));
        assertEquals("range-per-hour", limited.limit());
    }

    @Test
    void longestLimit_wins() {
        CodeLimits.Usage usage = new CodeLimits.Usage(
            sends(10, NOW.minus(Duration.ofHours(20))), sends(10, NOW.minusSeconds(1200)), SendHistory.EMPTY, 0);
        CodeLimits.Decision.Limited limited = assertInstanceOf(CodeLimits.Decision.Limited.class, limits.check(PHONE, CodeDelivery.SMS, usage, true, NOW));
        assertEquals("target-per-day", limited.limit());
    }

    @Test
    void onlyAllowedCountries_emailAlwaysAllowed() {
        assertTrue(limits.countryAllowed(PHONE));
        assertFalse(limits.countryAllowed(new CodeTarget(CodeTarget.Channel.SMS, "375291234567")));
        assertTrue(limits.countryAllowed(EMAIL));
        assertInstanceOf(CodeLimits.Decision.CountryNotAllowed.class,
            limits.check(new CodeTarget(CodeTarget.Channel.SMS, "375291234567"), CodeDelivery.SMS, usage(SendHistory.EMPTY, SendHistory.EMPTY), true, NOW));
    }

    @Test
    void budget_exhausted_blocksSms_notEmail() {
        CodeLimits.Usage spent = new CodeLimits.Usage(SendHistory.EMPTY, SendHistory.EMPTY, SendHistory.EMPTY, 99_800);
        assertInstanceOf(CodeLimits.Decision.BudgetExhausted.class, limits.check(PHONE, CodeDelivery.SMS, spent, false, NOW));
        CodeLimits.Decision.Allow email = assertInstanceOf(CodeLimits.Decision.Allow.class, limits.check(EMAIL, CodeDelivery.EMAIL, spent, false, NOW));
        assertEquals(0, email.cost());
    }

    @Test
    void budget_exhausted_callsStillGo_andCostLess() {
        CodeLimits.Usage spent = new CodeLimits.Usage(SendHistory.EMPTY, SendHistory.EMPTY, SendHistory.EMPTY, 100_000);
        CodeLimits.Decision.Allow call =
            assertInstanceOf(CodeLimits.Decision.Allow.class, limits.check(PHONE, CodeDelivery.CALL, spent, false, NOW));
        assertEquals(100, call.cost());
    }

    @Test
    void phoneChannels_callAndSmsByDefault_canTurnOffEither() {
        assertTrue(CodeLimitPolicy.DEFAULTS.allows(CodeDelivery.CALL));
        assertTrue(CodeLimitPolicy.DEFAULTS.allows(CodeDelivery.SMS));
        CodeLimitPolicy smsOnly = policy(Map.of("phone-channels", "sms"));
        assertFalse(smsOnly.allows(CodeDelivery.CALL));
        assertTrue(smsOnly.allows(CodeDelivery.EMAIL));
        assertThrows(IllegalArgumentException.class, () -> policy(Map.of("phone-channels", "email")));
        assertThrows(IllegalArgumentException.class, () -> policy(Map.of("phone-channels", "fax")));
    }

    @Test
    void budget_lastSmsThatFits_isAllowed() {
        CodeLimits.Usage spent = new CodeLimits.Usage(SendHistory.EMPTY, SendHistory.EMPTY, SendHistory.EMPTY, 99_600);
        assertInstanceOf(CodeLimits.Decision.Allow.class, limits.check(PHONE, CodeDelivery.SMS, spent, false, NOW));
    }

    @Test
    void alertPercent_80_then_100() {
        assertEquals(0, limits.alertPercent(79_999));
        assertEquals(80, limits.alertPercent(80_000));
        assertEquals(100, limits.alertPercent(100_000));
        assertEquals(0, new CodeLimits(policy(Map.of("daily-budget", "0"))).alertPercent(1_000_000));
    }

    @Test
    void policy_fromSettings_overridesOnlyGiven() {
        CodeLimitPolicy policy = policy(Map.of("cooldowns", "60, 60", "ip-per-hour", "3", "countries", "7,375", "zone", "UTC"));

        assertEquals(List.of(Duration.ofSeconds(60), Duration.ofSeconds(60)), policy.cooldowns());
        assertEquals(3, policy.ipPerHour());
        assertEquals(List.of("7", "375"), policy.countries());
        assertEquals(ZoneId.of("UTC"), policy.zone());
        assertEquals(5, policy.targetPerHour());
        assertEquals(400, policy.smsCost());
    }

    @Test
    void policy_badValue_isError_notSilentDefault() {
        assertThrows(NumberFormatException.class, () -> policy(Map.of("ip-per-hour", "много")));
    }

    @Test
    void history_roundTrip_andTrimsOld() {
        SendHistory history = SendHistory.EMPTY
            .plus(NOW.minus(Duration.ofHours(2)), CodeLimits.HOUR)
            .plus(NOW, CodeLimits.HOUR);
        // Отправка двухчасовой давности выпала при второй записи (храним час).
        assertEquals(List.of(NOW), history.sentAt());
        assertEquals(history, SendHistory.fromNotes(history.toNotes()));
        assertEquals(SendHistory.EMPTY, SendHistory.fromNotes(null));
    }

    private static CodeLimitPolicy policy(Map<String, String> settings) {
        return CodeLimitPolicy.from(settings::get, CodeLimitPolicy.DEFAULTS);
    }

    private static CodeLimits.Usage usage(SendHistory target, SendHistory ip) {
        return new CodeLimits.Usage(target, ip, SendHistory.EMPTY, 0);
    }

    // count отправок раз в минуту, начиная с first
    private static SendHistory sends(int count, Instant first) {
        SendHistory history = SendHistory.EMPTY;
        for (int i = 0; i < count; i++) {
            history = history.plus(first.plus(Duration.ofMinutes(i)), CodeLimits.DAY);
        }
        return history;
    }
}
