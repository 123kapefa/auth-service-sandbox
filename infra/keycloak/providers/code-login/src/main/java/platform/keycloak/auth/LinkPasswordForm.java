package platform.keycloak.auth;

import jakarta.ws.rs.core.MultivaluedMap;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.authenticators.broker.AbstractIdpAuthenticator;
import org.keycloak.authentication.authenticators.broker.IdpUsernamePasswordForm;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.UserModel;
import org.keycloak.services.validation.Validation;

/**
 * Привязка Госуслуг или Яндекс ID к уже существующей учётной записи: пароль именно от неё. Встроенная форма Keycloak
 * показывает поле логина (в него можно вписать другой аккаунт); здесь учётная запись известна заранее — та, с чьим
 * телефоном или почтой совпал человек, — и форма просит только пароль (usernameHidden: без «Войти по коду» и
 * без кнопок внешнего входа). Вход по паролю у неё выключен (SignInMethods) — пароль не подходит и здесь.
 */
public final class LinkPasswordForm extends IdpUsernamePasswordForm {

    public LinkPasswordForm(KeycloakSession session) {
        super(session);
    }

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        context.setUser(AbstractIdpAuthenticator.getExistingUser(context.getSession(), context.getRealm(), context.getAuthenticationSession()));
        super.authenticate(context);
    }

    @Override
    public boolean validatePassword(AuthenticationFlowContext context, UserModel user,
                                    MultivaluedMap<String, String> inputData, boolean clearUser) {
        return !SignInMethods.rejectPasswordOff(context, user,
                () -> challenge(context, "platformPasswordOff", Validation.FIELD_PASSWORD), clearUser)
            && super.validatePassword(context, user, inputData, clearUser)
            && !SignInMethods.rejectPassword(context, user,
                () -> challenge(context, getDefaultChallengeMessage(context), Validation.FIELD_PASSWORD), clearUser);
    }
}
