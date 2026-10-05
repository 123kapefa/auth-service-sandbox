package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.time.Instant;
import java.util.Map;

import org.junit.jupiter.api.Test;

class CodeStateTest {

    private static final Instant NOW = Instant.parse("2026-09-30T12:00:00Z");

    @Test
    void notes_roundTrip() {
        CodeState state = new CodeState("123456", NOW.plusSeconds(300), NOW.plusSeconds(60), 2, NOW.plusSeconds(10));

        assertEquals(state, CodeState.fromNotes(state.toNotes()));
    }

    @Test
    void notes_roundTrip_withoutOptionalFields() {
        CodeState state = new CodeState(null, null, NOW.plusSeconds(60), 0, null);

        assertEquals(state, CodeState.fromNotes(state.toNotes()));
    }

    @Test
    void noRecord_isEmpty() {
        assertEquals(CodeState.EMPTY, CodeState.fromNotes(null));
        assertEquals(CodeState.EMPTY, CodeState.fromNotes(Map.of()));
    }

    @Test
    void keep_untilLatestMoment() {
        CodeState state = new CodeState("123456", NOW.plusSeconds(300), NOW.plusSeconds(60), 5, NOW.plusSeconds(420));

        assertEquals(420, state.secondsToKeep(NOW));
    }

    @Test
    void keep_ignoresPast_andIsAtLeastOneSecond() {
        CodeState state = new CodeState(null, NOW.minusSeconds(5), NOW.minusSeconds(1), 0, null);

        assertEquals(1, state.secondsToKeep(NOW));
    }

    @Test
    void delivery_survivesStore() {
        CodeState state = new CodeState("1234", NOW.plusSeconds(300), NOW.plusSeconds(60), 0, null).sentAs("4321", CodeDelivery.CALL);

        CodeState restored = CodeState.fromNotes(state.toNotes());
        assertEquals("4321", restored.code());
        assertEquals(CodeDelivery.CALL, restored.delivery());
        assertEquals(null, CodeState.fromNotes(new CodeState("1", null, null, 0, null).toNotes()).delivery());
    }
}
