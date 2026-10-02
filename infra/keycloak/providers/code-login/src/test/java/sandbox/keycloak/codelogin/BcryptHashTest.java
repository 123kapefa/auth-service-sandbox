package sandbox.keycloak.codelogin;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.security.SecureRandom;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;
import org.junit.jupiter.params.provider.ValueSource;

/** Хэши — из BCrypt.Net-Next 4.0.3 (как в боевом сервисе): HashPassword(пароль, 12). */
class BcryptHashTest {

    private static final String LONG_72 = "a".repeat(72);
    private static final String LONG_HASH = "$2a$12$Wrpq205ZSLCWLZs4Buaco.ni/KQqZyXoQ0rtmWQKfxE17WKh591di"; // 72×a + "TAIL"

    @ParameterizedTest(name = "«{0}» подходит к хэшу BCrypt.Net")
    @CsvSource(delimiter = '|', value = {
        "Test1234!  | $2a$12$D8nEflo3yC0Zmv.jnkJN2uHNZsRqsZ7v0oaDTvdXcsvwwsqmE97pu",
        "пароль-Ёж  | $2a$12$wLppvJwJnQLNoAbZ.YRiJeChVuUXZuete5zydE1Eu/CQ1ANrFVjS2",
        "short      | $2a$12$/AT/wPXNlDiS6djNYnHytev1luf4GBsEglhn.Ak1pinFVf0bQmciq",
        "Test1234!  | $2a$04$.5TCG1/37KWHpTs1NM.IHeMbaWUfK.60q2nY2A1O47EikCeMKdBHq",
    })
    void passwordsFromDotNet_match(String password, String hash) {
        assertTrue(BcryptHash.matches(password, hash));
    }

    @ParameterizedTest(name = "«{0}» не подходит")
    @ValueSource(strings = {"test1234!", "Test1234", "Test1234! ", ""})
    void wrongPasswords_doNotMatch(String password) {
        assertFalse(BcryptHash.matches(password, "$2a$12$D8nEflo3yC0Zmv.jnkJN2uHNZsRqsZ7v0oaDTvdXcsvwwsqmE97pu"));
    }

    @Test
    void onlyFirst72Bytes_count_likeDotNet() {
        // BCrypt.Net: Verify(72×a) и Verify(72×a + "X") — true, Verify(71×a) — false.
        assertTrue(BcryptHash.matches(LONG_72, LONG_HASH));
        assertTrue(BcryptHash.matches(LONG_72 + "X", LONG_HASH));
        assertFalse(BcryptHash.matches("a".repeat(71), LONG_HASH));
    }

    @ParameterizedTest(name = "не BCrypt: «{0}» — просто не подходит")
    @ValueSource(strings = {"", "plain-password", "$2a$12$short", "$1$abc$def", "$2x$12$D8nEflo3yC0Zmv.jnkJN2uHNZsRqsZ7v0oaDTvdXcsvwwsqmE97pu"})
    void malformedHashes_doNotMatch(String hash) {
        assertFalse(BcryptHash.matches("Test1234!", hash));
    }

    @Test
    void nulls_doNotMatch() {
        assertFalse(BcryptHash.matches(null, "$2a$12$D8nEflo3yC0Zmv.jnkJN2uHNZsRqsZ7v0oaDTvdXcsvwwsqmE97pu"));
        assertFalse(BcryptHash.matches("Test1234!", null));
    }

    @Test
    void cost_isReadFromHash() {
        assertEquals(12, BcryptHash.cost("$2a$12$D8nEflo3yC0Zmv.jnkJN2uHNZsRqsZ7v0oaDTvdXcsvwwsqmE97pu"));
        assertEquals(4, BcryptHash.cost("$2a$04$.5TCG1/37KWHpTs1NM.IHeMbaWUfK.60q2nY2A1O47EikCeMKdBHq"));
    }

    @Test
    void newHash_isDotNetFormat_andMatches() {
        String hash = BcryptHash.hash("Test1234!", 4, new SecureRandom());
        assertTrue(hash.startsWith("$2a$04$"), hash);
        assertEquals(60, hash.length());
        assertTrue(BcryptHash.matches("Test1234!", hash));
        assertFalse(BcryptHash.matches("Test1234?", hash));
    }
}
