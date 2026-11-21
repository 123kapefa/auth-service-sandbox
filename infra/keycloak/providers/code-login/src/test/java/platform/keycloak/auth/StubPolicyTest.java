package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class StubPolicyTest {

    @Test
    void noStubs_anyMode_ok() {
        assertDoesNotThrow(() -> StubPolicy.require(false, false, false, false));
        assertDoesNotThrow(() -> StubPolicy.require(true, false, false, false));
    }

    @Test
    void stubs_devMode_ok() {
        assertDoesNotThrow(() -> StubPolicy.require(true, false, true, true));
    }

    @Test
    void stubs_notDev_explicitAllow_ok() {
        // kind и закрытый стенд работают в обычном start и разрешают заглушки явно.
        assertDoesNotThrow(() -> StubPolicy.require(false, true, true, true));
    }

    @Test
    void stubs_notDev_noAllow_stopsStartup_namingWhichOnes() {
        IllegalStateException both = assertThrows(IllegalStateException.class, () -> StubPolicy.require(false, false, true, true));
        assertTrue(both.getMessage().contains("captcha-stub") && both.getMessage().contains("phone-stub"));
        assertTrue(both.getMessage().contains("allow-stubs"));

        IllegalStateException onlyCaptcha = assertThrows(IllegalStateException.class, () -> StubPolicy.require(false, false, true, false));
        assertTrue(onlyCaptcha.getMessage().contains("captcha-stub") && !onlyCaptcha.getMessage().contains("phone-stub"));

        IllegalStateException onlyPhone = assertThrows(IllegalStateException.class, () -> StubPolicy.require(false, false, false, true));
        assertTrue(onlyPhone.getMessage().contains("phone-stub") && !onlyPhone.getMessage().contains("captcha-stub"));
    }
}
