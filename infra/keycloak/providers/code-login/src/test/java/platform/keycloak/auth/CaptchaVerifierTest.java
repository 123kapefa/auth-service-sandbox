package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;

import org.junit.jupiter.api.Test;

class CaptchaVerifierTest {

    private static final ObjectMapper JSON = new ObjectMapper();

    private static ObjectNode status(String value) {
        return JSON.createObjectNode().put("status", value);
    }

    @Test
    void ok_passed() {
        assertEquals(CaptchaVerifier.Answer.PASSED, CaptchaVerifier.answer(200, status("ok")));
    }

    @Test
    void failedOrGarbage_failed() {
        assertEquals(CaptchaVerifier.Answer.FAILED, CaptchaVerifier.answer(200, status("failed")));
        assertEquals(CaptchaVerifier.Answer.FAILED, CaptchaVerifier.answer(200, JSON.createObjectNode()));
        assertEquals(CaptchaVerifier.Answer.FAILED, CaptchaVerifier.answer(200, null));
    }

    @Test
    void clientError_failedNotPassed() {
        // Неверный серверный ключ или адрес — не повод пропускать любой токен.
        assertEquals(CaptchaVerifier.Answer.FAILED, CaptchaVerifier.answer(400, null));
        assertEquals(CaptchaVerifier.Answer.FAILED, CaptchaVerifier.answer(403, status("ok")));
        assertEquals(CaptchaVerifier.Answer.FAILED, CaptchaVerifier.answer(404, null));
    }

    @Test
    void serverError_unavailable() {
        assertEquals(CaptchaVerifier.Answer.UNAVAILABLE, CaptchaVerifier.answer(500, null));
        assertEquals(CaptchaVerifier.Answer.UNAVAILABLE, CaptchaVerifier.answer(503, null));
    }

    @Test
    void passes_outageMode_decidesOnlyForUnavailable() {
        for (CaptchaVerifier.OnOutage mode : CaptchaVerifier.OnOutage.values()) {
            assertTrue(CaptchaVerifier.passes(CaptchaVerifier.Answer.PASSED, mode));
            assertFalse(CaptchaVerifier.passes(CaptchaVerifier.Answer.FAILED, mode));
        }
        assertTrue(CaptchaVerifier.passes(CaptchaVerifier.Answer.UNAVAILABLE, CaptchaVerifier.OnOutage.PASS));
        assertFalse(CaptchaVerifier.passes(CaptchaVerifier.Answer.UNAVAILABLE, CaptchaVerifier.OnOutage.REJECT));
    }

    @Test
    void onOutage_parse_defaultPass_caseInsensitive_typoIsAnError() {
        assertEquals(CaptchaVerifier.OnOutage.PASS, CaptchaVerifier.OnOutage.parse(null));
        assertEquals(CaptchaVerifier.OnOutage.PASS, CaptchaVerifier.OnOutage.parse("  "));
        assertEquals(CaptchaVerifier.OnOutage.PASS, CaptchaVerifier.OnOutage.parse("pass"));
        assertEquals(CaptchaVerifier.OnOutage.REJECT, CaptchaVerifier.OnOutage.parse("Reject"));
        assertThrows(IllegalArgumentException.class, () -> CaptchaVerifier.OnOutage.parse("closed"));
    }
}
