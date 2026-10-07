package platform.keycloak.auth;

import java.time.LocalDate;

import jakarta.ws.rs.core.Response;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.Authenticator;
import org.keycloak.events.Errors;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.credential.OTPCredentialModel;
import org.keycloak.sessions.AuthenticationSessionModel;

/**
 * Последний шаг входа в браузере: доступ к приложению закрыт — понятная страница «Доступ закрыт», а не ошибка
 * уже в приложении. Шаг стоит после любого способа входа, в том числе входа по живой SSO-сессии.
 * Токены дополнительно сторожит AppAccessExecutor (refresh, обмен кода). Здесь же — правила проекта после входа
 * (обязательная 2FA, переходный период способа входа).
 */
public final class AppAccessAuthenticator implements Authenticator {

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        if (!AppAccess.isDenied(context.getAuthenticationSession().getClient(), context.getUser())) {
            projectRules(context);
            context.success();
            return;
        }

        context.getEvent().user(context.getUser());
        context.getEvent().error(Errors.ACCESS_DENIED);
        Response page = context.form().setError("platformAppAccessDenied").createErrorPage(Response.Status.FORBIDDEN);
        context.failure(AuthenticationFlowError.ACCESS_DENIED, page);
    }

    /**
     * Правила проекта после любого входа (ProjectSignIn): 2FA обязательна, а приложения-аутентификатора нет — сразу
     * завести (CONFIGURE_TOTP в этом входе); человек входит способом в переходном периоде — предложить ключ доступа
     * (PasskeyOfferAction, при каждом входе).
     */
    private static void projectRules(AuthenticationFlowContext context) {
        ProjectSignIn.Settings settings = ProjectSignInRealm.settings(context.getSession(), context.getRealm());
        if (settings.allOn()) {
            return;
        }
        UserModel user = context.getUser();
        AuthenticationSessionModel auth = context.getAuthenticationSession();
        if (settings.twoFactorRequired()
            && user.credentialManager().getStoredCredentialsByTypeStream(OTPCredentialModel.TYPE).findAny().isEmpty()) {
            auth.addRequiredAction(UserModel.RequiredAction.CONFIGURE_TOTP.name());
        }
        LocalDate today = ProjectSignInRealm.today();
        ProjectSignIn.transitional(settings, ProjectSignInRealm.user(context.getSession(), context.getRealm(), user), today)
            .stream().findFirst()
            .ifPresent(method -> auth.setAuthNote(PasskeyOfferAction.NOTE_TRANSITION, method.key() + ":"
                + ((ProjectSignIn.Status.Transition) settings.effective(method, today)).until()));
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
