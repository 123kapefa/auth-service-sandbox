package sandbox.keycloak.codelogin;

import jakarta.ws.rs.core.Response;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.Authenticator;
import org.keycloak.events.Errors;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;

/**
 * Последний шаг входа в браузере: доступ к приложению закрыт — понятная страница «Доступ закрыт», а не ошибка
 * уже в приложении. Шаг стоит после любого способа входа, в том числе входа по живой SSO-сессии.
 * Токены дополнительно сторожит AppAccessExecutor (refresh, обмен кода).
 */
public final class AppAccessAuthenticator implements Authenticator {

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        if (!AppAccess.isDenied(context.getAuthenticationSession().getClient(), context.getUser())) {
            context.success();
            return;
        }

        context.getEvent().user(context.getUser());
        context.getEvent().error(Errors.ACCESS_DENIED);
        Response page = context.form().setError("sandboxAppAccessDenied").createErrorPage(Response.Status.FORBIDDEN);
        context.failure(AuthenticationFlowError.ACCESS_DENIED, page);
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        // формы нет
    }

    @Override
    public boolean requiresUser() {
        return true;
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
