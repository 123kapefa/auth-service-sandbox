package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.Base64;
import java.util.Map;

import org.junit.jupiter.api.Test;

class EsiaRequestTest {

    @Test
    void timestamp_isMoscowTimeWithOffset() {
        assertEquals("2026.10.05 20:30:00 +0300", EsiaRequest.timestamp(Instant.parse("2026-10-05T17:30:00Z")));
    }

    @Test
    void signedTexts_areConcatenatedInProtocolOrder() {
        String authorize = EsiaRequest.authorizeText("SYS", "openid mobile", "2026.10.05 20:30:00 +0300", "uuid", "http://kc/cb");
        assertEquals("SYSopenid mobile2026.10.05 20:30:00 +0300uuidhttp://kc/cb", authorize);
        assertEquals(authorize + "CODE",
            EsiaRequest.tokenText("SYS", "openid mobile", "2026.10.05 20:30:00 +0300", "uuid", "http://kc/cb", "CODE"));
    }

    @Test
    void signature_becomesUrlSafeWithoutPadding() {
        byte[] raw = {(byte) 0xfb, (byte) 0xff, 0x01, 0x02};
        String base64 = Base64.getEncoder().encodeToString(raw); // "+/8BAg=="
        assertEquals("-_8BAg", EsiaRequest.urlSafe(base64 + "\n"));
    }

    @Test
    void oid_isReadFromAccessToken() {
        String payload = Base64.getUrlEncoder().withoutPadding()
            .encodeToString("{\"urn:esia:sbj_id\":1000299654,\"scope\":\"openid\"}".getBytes(StandardCharsets.UTF_8));
        assertEquals("1000299654", EsiaRequest.oid("eyJhbGciOiJHT1NUMzQxMCJ9." + payload + ".sig"));
    }

    @Test
    void oid_missingOrNotJwt_isRejected() {
        String payload = Base64.getUrlEncoder().withoutPadding().encodeToString("{}".getBytes(StandardCharsets.UTF_8));
        assertThrows(IllegalArgumentException.class, () -> EsiaRequest.oid("h." + payload + ".s"));
        assertThrows(IllegalArgumentException.class, () -> EsiaRequest.oid("opaque-token"));
    }

    @Test
    void authorizeQuery_encodesPlusAndSpaces() {
        assertEquals("timestamp=2026.10.05+20%3A30%3A00+%2B0300&scope=openid+mobile",
            EsiaIdentityProvider.encode(new java.util.LinkedHashMap<>(Map.of("timestamp", "2026.10.05 20:30:00 +0300")) {{
                put("scope", "openid mobile");
            }}));
    }
}
