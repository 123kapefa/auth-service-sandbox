package sandbox.keycloak.codelogin;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.Authenticator;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;

/** Заготовка: в сценарий входа пока не подключена, ничего не делает. */
final class CodeLoginAuthenticator implements Authenticator {

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        context.attempted();
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        context.attempted();
    }

    @Override
    public boolean requiresUser() {
        return false;
    }

    @Override
    public boolean configuredFor(KeycloakSession session, RealmModel realm, UserModel user) {
        return true;
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
