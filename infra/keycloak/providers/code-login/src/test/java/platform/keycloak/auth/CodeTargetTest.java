package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.junit.jupiter.params.provider.ValueSource;

class CodeTargetTest {

    @ParameterizedTest(name = "«{0}» → {1} {2}")
    @CsvSource(delimiter = '|', value = {
        "+7 (999) 000-00-01          | SMS   | 79990000001",
        "8 999 000 00 01             | SMS   | 79990000001",
        "customer@sandbox.test       | EMAIL | customer@sandbox.test",
        "'  Customer@Sandbox.TEST  ' | EMAIL | customer@sandbox.test",
    })
    void channel_byInput(String input, CodeTarget.Channel channel, String value) {
        assertEquals(new CodeTarget(channel, value), CodeTarget.parse(input).orElseThrow());
    }

    @ParameterizedTest(name = "«{0}» — ни номер, ни почта")
    @ValueSource(strings = {"", "   ", "abc", "12345", "user@", "@sandbox.test", "user@host", "a b@sandbox.test"})
    void neither_isRejected(String input) {
        assertTrue(CodeTarget.parse(input).isEmpty());
    }

    @Test
    void null_isRejected() {
        assertTrue(CodeTarget.parse(null).isEmpty());
    }

    @Test
    void display_phoneWithPlus_emailAsIs() {
        assertEquals("+79990000001", new CodeTarget(CodeTarget.Channel.SMS, "79990000001").display());
        assertEquals("a@sandbox.test", new CodeTarget(CodeTarget.Channel.EMAIL, "a@sandbox.test").display());
    }

    @Test
    void masked_hidesMostOfAddress() {
        assertEquals("+7 *** ***-**-96", new CodeTarget(CodeTarget.Channel.SMS, "79990000096").masked());
        assertEquals("+*** ***-**-67", new CodeTarget(CodeTarget.Channel.SMS, "375291234567").masked());
        assertEquals("s***@sandbox.test", new CodeTarget(CodeTarget.Channel.EMAIL, "sign-in@sandbox.test").masked());
    }
}
