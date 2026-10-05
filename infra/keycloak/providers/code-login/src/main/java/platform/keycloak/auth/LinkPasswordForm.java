package platform.keycloak.auth;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.authenticators.broker.AbstractIdpAuthenticator;
import org.keycloak.authentication.authenticators.broker.IdpUsernamePasswordForm;
import org.keycloak.models.KeycloakSession;

/**
 * Привязка Госуслуг или Яндекс ID к уже существующей учётной записи: пароль именно от неё. Встроенная форма Keycloak
 * показывает поле логина (в него можно вписать другой аккаунт); здесь учётная запись известна заранее — та, с чьим
 * телефоном или почтой совпал человек, — и форма просит только пароль (usernameHidden: без «Войти по коду» и
 * без кнопок внешнего входа).
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
}
