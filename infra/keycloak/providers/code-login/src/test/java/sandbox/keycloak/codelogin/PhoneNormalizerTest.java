package sandbox.keycloak.codelogin;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.junit.jupiter.params.provider.ValueSource;

class PhoneNormalizerTest {

    @ParameterizedTest(name = "«{0}» → {1}")
    @CsvSource(delimiter = '|', value = {
        "79990000001          | 79990000001",
        "+79990000001         | 79990000001",
        "+7 (999) 000-00-01   | 79990000001",
        "8 999 000 00 01      | 79990000001",   // 8XXX → 7XXX
        "9990000001           | 79990000001",   // 10 цифр → 7 + номер
        "(999) 000-00-01      | 79990000001",
        "+44 20 7946 0958     | 442079460958",  // иностранный — цифры как есть
    })
    void phones_areNormalizedToLogin(String input, String expected) {
        assertEquals(expected, PhoneNormalizer.toLogin(input).orElseThrow());
    }

    @ParameterizedTest(name = "«{0}» — не номер")
    @ValueSource(strings = {"customer@sandbox.test", "", "   ", "+7 999", "12345", "abc123", "7999000000112345678"})
    void notPhones_areRejected(String input) {
        assertTrue(PhoneNormalizer.toLogin(input).isEmpty());
    }

    @Test
    void null_isNotPhone() {
        assertTrue(PhoneNormalizer.toLogin(null).isEmpty());
    }

    @Test
    void e164_addsPlus() {
        assertEquals("+79990000001", PhoneNormalizer.toE164("79990000001"));
    }
}
