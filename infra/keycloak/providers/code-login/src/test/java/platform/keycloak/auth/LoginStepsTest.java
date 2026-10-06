package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import java.util.Optional;
import java.util.Set;

import org.junit.jupiter.api.Test;

class LoginStepsTest {

    private static final Optional<CodeTarget.Channel> PHONE = Optional.of(CodeTarget.Channel.SMS);
    private static final Optional<CodeTarget.Channel> EMAIL = Optional.of(CodeTarget.Channel.EMAIL);
    private static final String PASSKEY = LoginSteps.PASSKEY;
    private static final String EMAIL_CODE = SignInMethods.EMAIL_CODE;
    private static final String PHONE_CODE = SignInMethods.PHONE_CODE;

    // Почта и номер-логин есть; ключ доступа и выключенное — по параметрам.
    private static LoginSteps.Account account(boolean passkey, String... disabled) {
        return new LoginSteps.Account(true, Set.of(disabled), passkey, true, true);
    }

    @Test
    void unknownLogin_looksLikePasswordEnabled() {
        LoginSteps.View unknown = LoginSteps.view(PHONE, null, true);
        LoginSteps.View known = LoginSteps.view(PHONE, account(true), true);
        assertEquals(new LoginSteps.View(true, List.of(PASSKEY, PHONE_CODE)), unknown);
        assertEquals(unknown, known);
    }

    @Test
    void passwordView_dependsOnlyOnWhatWasTyped() {
        // Выключенный код на телефон и почта в аккаунте не видны: иначе по экрану стало бы понятно, что аккаунт есть.
        LoginSteps.Account phoneOff = account(true, PHONE_CODE);
        assertEquals(LoginSteps.view(PHONE, null, true), LoginSteps.view(PHONE, phoneOff, true));
        assertEquals(new LoginSteps.View(true, List.of(PASSKEY, EMAIL_CODE)), LoginSteps.view(EMAIL, phoneOff, true));
    }

    @Test
    void loginThatIsNeitherPhoneNorEmail_onlyPassword() {
        assertEquals(new LoginSteps.View(true, List.of()), LoginSteps.view(Optional.empty(), null, false));
    }

    @Test
    void passwordDisabled_methodsByPrice() {
        LoginSteps.View view = LoginSteps.view(PHONE, account(true, SignInMethods.PASSWORD), true);
        assertFalse(view.password());
        assertEquals(List.of(PASSKEY, EMAIL_CODE, PHONE_CODE), view.methods());
        assertTrue(view.autoStart().isEmpty());
    }

    @Test
    void passwordDisabled_disabledCodesHidden() {
        LoginSteps.View view = LoginSteps.view(EMAIL, account(true, SignInMethods.PASSWORD, PHONE_CODE), true);
        assertEquals(List.of(PASSKEY, EMAIL_CODE), view.methods());
    }

    @Test
    void oneMethod_opensItself() {
        LoginSteps.View view = LoginSteps.view(PHONE, account(true, SignInMethods.PASSWORD, PHONE_CODE, EMAIL_CODE), true);
        assertEquals(Optional.of(PASSKEY), view.autoStart());

        LoginSteps.Account noPasskeyNoPhone = new LoginSteps.Account(true, Set.of(SignInMethods.PASSWORD), false, true, false);
        assertEquals(Optional.of(EMAIL_CODE), LoginSteps.view(PHONE, noPasskeyNoPhone, true).autoStart());
    }

    @Test
    void phoneCode_onlyWhenLoginIsPhone() {
        // Номер в атрибуте, а логин — почта: код по номеру нашёл бы другого пользователя (или создал нового).
        LoginSteps.Account emailLogin = new LoginSteps.Account(true, Set.of(SignInMethods.PASSWORD), true, true, false);
        assertEquals(List.of(PASSKEY, EMAIL_CODE), LoginSteps.view(EMAIL, emailLogin, true).methods());
    }

    @Test
    void passkeysOffInRealm_notOffered() {
        assertEquals(List.of(EMAIL_CODE, PHONE_CODE),
            LoginSteps.view(PHONE, account(true, SignInMethods.PASSWORD), false).methods());
    }

    @Test
    void disabledAccount_looksLikeUnknown() {
        LoginSteps.Account off = new LoginSteps.Account(false, Set.of(SignInMethods.PASSWORD), true, true, true);
        assertEquals(LoginSteps.view(PHONE, null, true), LoginSteps.view(PHONE, off, true));
    }

    @Test
    void passwordView_neverAutoStarts() {
        assertTrue(LoginSteps.view(Optional.empty(), null, true).autoStart().isEmpty());
    }
}
