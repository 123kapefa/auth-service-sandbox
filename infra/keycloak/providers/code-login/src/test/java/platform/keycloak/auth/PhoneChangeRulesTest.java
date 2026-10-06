package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Instant;
import java.util.Optional;
import java.util.Set;

import org.junit.jupiter.api.Test;

class PhoneChangeRulesTest {

    private static final Instant NOW = Instant.parse("2026-10-06T12:00:00Z");
    private static final String CURRENT = "79990000001";

    private static PhoneChangeRules.Check check(String input, String changedAt, String... taken) {
        return PhoneChangeRules.check(input, CURRENT, changedAt, NOW, Set.of(taken)::contains);
    }

    @Test
    void newFreeNumber_ok_asLogin() {
        assertEquals(new PhoneChangeRules.Check.Ok("79990000002"), check("+7 (999) 000-00-02", null));
        assertEquals(new PhoneChangeRules.Check.Ok("79990000002"), check("8 999 000 00 02", null));
    }

    @Test
    void notAPhone_invalid() {
        assertEquals(new PhoneChangeRules.Check.Invalid(), check("abc", null));
        assertEquals(new PhoneChangeRules.Check.Invalid(), check("12345", null));
    }

    @Test
    void sameNumber_rejected() {
        assertEquals(new PhoneChangeRules.Check.Same(), check("9990000001", null));
    }

    @Test
    void numberOfAnotherAccount_taken() {
        assertEquals(new PhoneChangeRules.Check.Taken(), check("79990000002", null, "79990000002"));
    }

    @Test
    void changedLessThanDayAgo_tooOften_evenBeforeNumberChecks() {
        assertEquals(new PhoneChangeRules.Check.TooOften(Instant.parse("2026-10-07T02:00:00Z")),
            check("abc", "2026-10-06T02:00:00Z"));
    }

    @Test
    void changedDayAgo_allowedAgain() {
        assertEquals(new PhoneChangeRules.Check.Ok("79990000002"), check("79990000002", "2026-10-05T12:00:00Z"));
    }

    @Test
    void brokenTimestamp_doesNotBlock() {
        assertTrue(PhoneChangeRules.nextAllowed("вчера", NOW).isEmpty());
        assertEquals(Optional.empty(), PhoneChangeRules.nextAllowed(null, NOW));
    }

    @Test
    void noCurrentPhone_anyValidNumberOk() {
        assertEquals(new PhoneChangeRules.Check.Ok("79990000001"),
            PhoneChangeRules.check("79990000001", null, null, NOW, login -> false));
    }
}
