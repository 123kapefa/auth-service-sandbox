package sandbox.keycloak.codelogin;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.authenticators.conditional.ConditionalAuthenticator;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;

/** Условие подсценария «вход по коду»: на форме входа нажали «Войти по коду». */
final class CodeRequestedCondition implements ConditionalAuthenticator {

    static final CodeRequestedCondition SINGLETON = new CodeRequestedCondition();

    @Override
    public boolean matchCondition(AuthenticationFlowContext context) {
        return context.getAuthenticationSession().getAuthNote(CodeLoginNotes.TARGET) != null;
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        // условие ничего не показывает
    }

    @Override
    public boolean requiresUser() {
        return false;
    }

    @Override
    public void setRequiredActions(KeycloakSession session, RealmModel realm, UserModel user) {
        // обязательных действий нет
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
