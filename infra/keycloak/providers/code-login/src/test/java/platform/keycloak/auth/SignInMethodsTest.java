package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.util.Locale;
import java.util.Properties;
import java.util.Set;

import org.junit.jupiter.api.Test;

class SignInMethodsTest {

    private static final String PASSWORD = SignInMethods.PASSWORD;
    private static final String PHONE = SignInMethods.PHONE_CODE;
    private static final String EMAIL = SignInMethods.EMAIL_CODE;

    // Пароль, телефон и почта есть; ключ доступа, аутентификатор и внешний вход — по параметрам.
    private static SignInMethods.Facts facts(boolean passkey, boolean authenticator, boolean external, Set<String> disabled) {
        return new SignInMethods.Facts(true, true, true, passkey, authenticator, external, disabled);
    }

    @Test
    void parse_skipsUnknownAndBlank() {
        assertEquals(Set.of(PASSWORD, EMAIL), SignInMethods.parse(" password, ,email-code,passkey"));
        assertEquals(Set.of(), SignInMethods.parse(null));
        assertEquals(Set.of(), SignInMethods.parse(" "));
    }

    @Test
    void methodsOffInProject_doNotCountAsLeft_nothingIsDisabled() {
        // Аутентификатор держит выключенные пароль и телефон, а почту проект выключил: войти нечем — страховка снимает всё.
        SignInMethods.Facts facts = new SignInMethods.Facts(true, true, true, false, true, false,
            Set.of(PASSWORD, PHONE), Set.of(EMAIL));

        assertEquals(Set.of(), SignInMethods.effectiveDisabled(facts));
    }

    @Test
    void methodOffInProject_otherLeft_disabledStays() {
        SignInMethods.Facts facts = new SignInMethods.Facts(true, true, true, false, true, false,
            Set.of(PASSWORD), Set.of(EMAIL));

        assertEquals(Set.of(PASSWORD), SignInMethods.effectiveDisabled(facts));
    }

    @Test
    void passwordAndPhone_disabled_whilePasskeyExists() {
        assertEquals(Set.of(PASSWORD, PHONE),
            SignInMethods.effectiveDisabled(facts(true, false, false, Set.of(PASSWORD, PHONE))));
    }

    @Test
    void passwordAndPhone_disabled_whileAuthenticatorExists() {
        assertEquals(Set.of(PASSWORD, PHONE),
            SignInMethods.effectiveDisabled(facts(false, true, false, Set.of(PASSWORD, PHONE))));
    }

    @Test
    void passwordAndPhone_comeBack_whenPasskeyAndAuthenticatorAreGone() {
        assertEquals(Set.of(EMAIL),
            SignInMethods.effectiveDisabled(facts(false, false, false, Set.of(PASSWORD, PHONE, EMAIL))));
    }

    @Test
    void emailCode_disabled_withoutPasskey() {
        assertEquals(Set.of(EMAIL), SignInMethods.effectiveDisabled(facts(false, false, false, Set.of(EMAIL))));
    }

    @Test
    void allDisabled_withPasskey_staysDisabled() {
        assertEquals(Set.of(PASSWORD, PHONE, EMAIL),
            SignInMethods.effectiveDisabled(facts(true, false, false, Set.of(PASSWORD, PHONE, EMAIL))));
    }

    @Test
    void allDisabled_withOnlyAuthenticator_nothingDisabled() {
        // Аутентификатор — второй шаг, а не способ входа: без ключа и внешнего входа войти было бы нечем.
        assertEquals(Set.of(), SignInMethods.effectiveDisabled(facts(false, true, false, Set.of(PASSWORD, PHONE, EMAIL))));
    }

    @Test
    void allDisabled_withExternalLoginAndAuthenticator_staysDisabled() {
        assertEquals(Set.of(PASSWORD, PHONE, EMAIL),
            SignInMethods.effectiveDisabled(facts(false, true, true, Set.of(PASSWORD, PHONE, EMAIL))));
    }

    @Test
    void missingMethods_doNotCountAsLeft() {
        // Ни пароля, ни почты: остался бы только код на телефон — его выключить нельзя.
        SignInMethods.Facts phoneOnly = new SignInMethods.Facts(false, true, false, false, true, false, Set.of(PHONE));
        assertEquals(Set.of(), SignInMethods.effectiveDisabled(phoneOnly));
    }

    @Test
    void notice_text() {
        Properties messages = new Properties();
        messages.setProperty("platformSignInMethodsDisabledText", "В {0} выключен вход {1}.");
        messages.setProperty("platformSignInMethodPassword", "по паролю");
        messages.setProperty("platformSignInMethodsResetText", "Администратор {0} вернул способы входа.");
        AccountNoticeResource.Notice disabled = new AccountNoticeResource.Notice();
        disabled.event = "disabled";
        disabled.method = PASSWORD;
        AccountNoticeResource.Notice reset = new AccountNoticeResource.Notice();
        reset.event = "reset";

        Locale ru = Locale.forLanguageTag("ru");
        assertEquals("В Магазин выключен вход по паролю.", AccountNoticeResource.text(messages, ru, disabled, "Магазин"));
        assertEquals("Администратор Магазин вернул способы входа.", AccountNoticeResource.text(messages, ru, reset, "Магазин"));
    }
}
