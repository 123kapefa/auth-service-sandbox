package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class PhoneProvidersTest {

    @Test
    void providerUrl_httpProvider_evenWithStubFlag() {
        assertInstanceOf(HttpPhoneProvider.class, PhoneProviders.choose("http://gateway.example/phone", false));
        assertInstanceOf(HttpPhoneProvider.class, PhoneProviders.choose("http://gateway.example/phone", true));
    }

    @Test
    void noSettings_noPhone_notLogStub() {
        assertInstanceOf(NoPhoneProvider.class, PhoneProviders.choose(null, false));
        assertInstanceOf(NoPhoneProvider.class, PhoneProviders.choose("", false));
        assertInstanceOf(NoPhoneProvider.class, PhoneProviders.choose("   ", false));
    }

    @Test
    void stubFlag_logStub_onlyWhenExplicit() {
        assertInstanceOf(LogPhoneProvider.class, PhoneProviders.choose(null, true));
        assertInstanceOf(LogPhoneProvider.class, PhoneProviders.choose("", true));
    }

    @Test
    void onlyNoPhone_isUnavailable() {
        assertFalse(new NoPhoneProvider().available());
        assertTrue(new LogPhoneProvider().available());
        assertTrue(new HttpPhoneProvider("http://gateway.example/phone").available());
    }

    @Test
    void noPhone_sendsNothing() {
        PhoneProvider none = new NoPhoneProvider();

        assertTrue(none.call(null, "79990000001").isEmpty());
        assertFalse(none.sms(null, "79990000001", "123456"));
        assertFalse(none.message(null, "79990000001", "текст"));
    }
}
