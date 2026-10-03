package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.util.Random;

import org.junit.jupiter.api.Test;

class CodeRulesTest {

    private final MovableClock clock = new MovableClock(Instant.parse("2026-09-29T12:00:00Z"));
    private final CodeRules rules = new CodeRules(CodePolicy.PRODUCTION, clock, new Random(42));

    @Test
    void send_createsSixDigitCode_validForFiveMinutes() {
        CodeRules.SendResult.Sent sent = assertInstanceOf(CodeRules.SendResult.Sent.class, rules.send(CodeState.EMPTY));

        assertTrue(sent.code().matches("\\d{6}"));
        assertEquals(clock.instant().plusSeconds(300), sent.state().expiresAt());
        assertEquals(clock.instant().plusSeconds(60), sent.state().resendAllowedAt());
    }

    @Test
    void resend_before60Seconds_isTooEarly_after60_isAllowed() {
        CodeState state = sent(CodeState.EMPTY).state();

        clock.advance(Duration.ofSeconds(59));
        CodeRules.SendResult.TooEarly early = assertInstanceOf(CodeRules.SendResult.TooEarly.class, rules.send(state));
        assertEquals(1, early.retryAfterSeconds());

        clock.advance(Duration.ofSeconds(1));
        assertInstanceOf(CodeRules.SendResult.Sent.class, rules.send(state));
    }

    @Test
    void newCode_replacesOld() {
        CodeRules.SendResult.Sent first = sent(CodeState.EMPTY);
        clock.advance(Duration.ofSeconds(60));
        CodeRules.SendResult.Sent second = sent(first.state());

        assertNotEquals(first.code(), second.code());
        assertInstanceOf(CodeRules.VerifyResult.Wrong.class, rules.verify(second.state(), first.code()));
    }

    @Test
    void correctCode_succeeds_andCannotBeReused() {
        CodeRules.SendResult.Sent sent = sent(CodeState.EMPTY);

        CodeRules.VerifyResult.Success success =
            assertInstanceOf(CodeRules.VerifyResult.Success.class, rules.verify(sent.state(), sent.code()));

        assertNull(success.state().code());
        assertInstanceOf(CodeRules.VerifyResult.NoActiveCode.class, rules.verify(success.state(), sent.code()));
    }

    @Test
    void expiredCode_isRejected() {
        CodeRules.SendResult.Sent sent = sent(CodeState.EMPTY);

        clock.advance(Duration.ofSeconds(300));

        assertInstanceOf(CodeRules.VerifyResult.NoActiveCode.class, rules.verify(sent.state(), sent.code()));
    }

    @Test
    void fiveWrongCodes_burnCode_andLockForFiveMinutes() {
        CodeRules.SendResult.Sent sent = sent(CodeState.EMPTY);
        CodeState state = sent.state();

        for (int attempt = 1; attempt <= 4; attempt++) {
            CodeRules.VerifyResult.Wrong wrong =
                assertInstanceOf(CodeRules.VerifyResult.Wrong.class, rules.verify(state, "000000"));
            assertEquals(5 - attempt, wrong.attemptsLeft());
            state = wrong.state();
        }

        CodeRules.VerifyResult.LockedNow locked =
            assertInstanceOf(CodeRules.VerifyResult.LockedNow.class, rules.verify(state, "000000"));
        state = locked.state();
        assertEquals(300, locked.retryAfterSeconds());

        // Даже верный код больше не принимается, и новый не отправить, пока идёт блокировка.
        assertInstanceOf(CodeRules.VerifyResult.Locked.class, rules.verify(state, sent.code()));
        assertInstanceOf(CodeRules.SendResult.Locked.class, rules.send(state));

        clock.advance(Duration.ofSeconds(300));
        assertInstanceOf(CodeRules.SendResult.Sent.class, rules.send(state));
    }

    @Test
    void newCode_resetsFailedAttempts() {
        CodeState state = sent(CodeState.EMPTY).state();
        state = assertInstanceOf(CodeRules.VerifyResult.Wrong.class, rules.verify(state, "000000")).state();

        clock.advance(Duration.ofSeconds(60));
        CodeState fresh = sent(state).state();

        assertEquals(0, fresh.failedAttempts());
    }

    @Test
    void codeWithSpaces_isAccepted() {
        CodeRules.SendResult.Sent sent = sent(CodeState.EMPTY);

        assertInstanceOf(CodeRules.VerifyResult.Success.class, rules.verify(sent.state(), " " + sent.code() + " "));
    }

    private CodeRules.SendResult.Sent sent(CodeState state) {
        return assertInstanceOf(CodeRules.SendResult.Sent.class, rules.send(state));
    }

    /** Часы, которые тест двигает вперёд: 5 минут проверяются мгновенно. */
    private static final class MovableClock extends Clock {
        private Instant now;

        MovableClock(Instant start) {
            now = start;
        }

        void advance(Duration duration) {
            now = now.plus(duration);
        }

        @Override
        public Instant instant() {
            return now;
        }

        @Override
        public ZoneOffset getZone() {
            return ZoneOffset.UTC;
        }

        @Override
        public Clock withZone(ZoneId zone) {
            return this;
        }
    }
}
