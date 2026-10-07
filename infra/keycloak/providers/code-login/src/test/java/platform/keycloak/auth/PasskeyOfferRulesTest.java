package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Instant;

import org.junit.jupiter.api.Test;

class PasskeyOfferRulesTest {

    private static final Instant NOW = Instant.parse("2026-10-06T12:00:00Z");

    @Test
    void noPasskeyNeverSkipped_offered() {
        assertTrue(PasskeyOfferRules.shouldOffer(true, false, null, NOW));
        assertTrue(PasskeyOfferRules.shouldOffer(true, false, "", NOW));
    }

    @Test
    void passkeysOffOrAlreadyHasOne_notOffered() {
        assertFalse(PasskeyOfferRules.shouldOffer(false, false, null, NOW));
        assertFalse(PasskeyOfferRules.shouldOffer(true, true, null, NOW));
    }

    @Test
    void skippedLessThan30DaysAgo_notOffered() {
        assertFalse(PasskeyOfferRules.shouldOffer(true, false, "2026-10-06T11:00:00Z", NOW));
        assertFalse(PasskeyOfferRules.shouldOffer(true, false, "2026-09-06T12:00:01Z", NOW));
    }

    @Test
    void skipped30DaysAgoOrMore_offeredAgain() {
        assertTrue(PasskeyOfferRules.shouldOffer(true, false, "2026-09-06T12:00:00Z", NOW));
        assertTrue(PasskeyOfferRules.shouldOffer(true, false, "2026-01-01T00:00:00Z", NOW));
    }

    @Test
    void unreadableSkipTime_offered() {
        assertTrue(PasskeyOfferRules.shouldOffer(true, false, "вчера", NOW));
    }
}
