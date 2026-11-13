package platform.keycloak.auth;

import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.authenticators.browser.AbstractUsernameFormAuthenticator;
import org.keycloak.authentication.authenticators.resetcred.ResetCredentialChooseUser;
import org.keycloak.events.Details;
import org.keycloak.events.Errors;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.utils.FormMessage;
import org.keycloak.services.messages.Messages;
import org.keycloak.services.validation.Validation;

/**
 * «Забыли пароль?»: выбор пользователя по номеру или почте, номер приводится к логину на сервере (#57). «+7 (999)
 * 000-00-01», «8 999 000 00 01» и «9990000001» — один пользователь 79990000001, как на входе (PasswordLoginForm).
 *
 * Показ формы и случаи «уже известный пользователь» и «есть сессия» — встроенного ResetCredentialChooseUser; здесь
 * только action: он читает логин из формы и сам привести его не даёт. Правила встроенного сохранены: не нашли или
 * отключён — не говорим об этом (шаг успешен без пользователя, письмо не уходит, ответ одинаковый).
 */
public final class ResetChooseUser implements Authenticator {

    private final ResetCredentialChooseUser builtIn = new ResetCredentialChooseUser();

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        builtIn.authenticate(context);
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        String typed = formData.getFirst(Validation.FIELD_USERNAME);
        if (typed == null || typed.isBlank()) {
            context.getEvent().error(Errors.USERNAME_MISSING);
            Response challenge = context.form()
                .addError(new FormMessage(Validation.FIELD_USERNAME, Messages.MISSING_USERNAME))
                .createPasswordReset();
            context.failureChallenge(AuthenticationFlowError.INVALID_USER, challenge);
            return;
        }

        String username = PhoneNormalizer.normalizeLogin(typed);
        RealmModel realm = context.getRealm();
        UserModel user = context.getSession().users().getUserByUsername(realm, username);
        if (user == null && realm.isLoginWithEmailAllowed() && username.contains("@")) {
            user = context.getSession().users().getUserByEmail(realm, username);
        }

        context.getAuthenticationSession().setAuthNote(AbstractUsernameFormAuthenticator.ATTEMPTED_USERNAME, username);

        if (user == null) {
            context.getEvent().clone().detail(Details.USERNAME, username).error(Errors.USER_NOT_FOUND);
            context.clearUser();
        } else if (!user.isEnabled()) {
            context.getEvent().clone().detail(Details.USERNAME, username).user(user).error(Errors.USER_DISABLED);
            context.clearUser();
        } else {
            context.setUser(user);
        }
        context.success();
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
        // как у встроенного: ничего
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
