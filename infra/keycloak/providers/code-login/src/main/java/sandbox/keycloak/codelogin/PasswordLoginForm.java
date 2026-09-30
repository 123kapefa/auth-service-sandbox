package sandbox.keycloak.codelogin;

import jakarta.ws.rs.core.MultivaluedHashMap;
import jakarta.ws.rs.core.MultivaluedMap;

import java.util.Optional;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.authenticators.browser.UsernamePasswordForm;
import org.keycloak.models.KeycloakSession;
import org.keycloak.events.Details;
import org.keycloak.events.Errors;
import org.keycloak.services.managers.AuthenticationManager;
import org.keycloak.services.validation.Validation;

/**
 * Обычная форма «Телефон или почта + пароль» (встроенный UsernamePasswordForm), но номер приводится к логину
 * на сервере: «+7 (999) 000-00-01», «8 999 000 00 01» и «9990000001» — один пользователь 79990000001.
 * Скрипт темы только показывает «+»; запрос в обход формы тоже найдёт пользователя.
 *
 * Кнопка «Войти по коду» (codeLogin) на той же форме: пароль не нужен, номер или почта запоминается в сессии входа,
 * дальше — подсценарий «вход по коду» (CodeRequestedCondition → CodeLoginAuthenticator).
 */
public final class PasswordLoginForm extends UsernamePasswordForm {

    public PasswordLoginForm(KeycloakSession session) {
        super(session);
    }

    static final String BUTTON_CODE_LOGIN = "codeLogin";

    @Override
    public void action(AuthenticationFlowContext context) {
        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        if (formData.containsKey(BUTTON_CODE_LOGIN)) {
            requestCode(context, formData);
            return;
        }
        CodeLoginNotes.clear(context.getAuthenticationSession());
        super.action(context);
    }

    private void requestCode(AuthenticationFlowContext context, MultivaluedMap<String, String> formData) {
        Optional<CodeTarget> target = CodeTarget.parse(formData.getFirst(AuthenticationManager.FORM_USERNAME));
        if (target.isEmpty()) {
            context.getEvent().error(Errors.USERNAME_MISSING);
            context.failureChallenge(AuthenticationFlowError.INVALID_USER,
                challenge(context, "sandboxCodeLoginTargetRequired", Validation.FIELD_USERNAME));
            return;
        }

        // Есть ли такой пользователь, здесь не проверяем и не показываем: экран кода — одинаковый для всех.
        context.clearUser();
        context.getEvent().detail(Details.USERNAME, target.get().value());
        context.getAuthenticationSession().setAuthNote(ATTEMPTED_USERNAME, target.get().value());
        CodeLoginNotes.save(context.getAuthenticationSession(), target.get());
        context.success();
    }

    @Override
    protected boolean validateForm(AuthenticationFlowContext context, MultivaluedMap<String, String> formData) {
        return validateUserAndPassword(context, withNormalizedLogin(formData));
    }

    // Копия данных формы с логином вместо введённого текста; остальные поля (пароль, rememberMe) — как пришли.
    private static MultivaluedMap<String, String> withNormalizedLogin(MultivaluedMap<String, String> formData) {
        MultivaluedMap<String, String> copy = new MultivaluedHashMap<>(formData);
        String username = formData.getFirst(AuthenticationManager.FORM_USERNAME);
        if (username != null) {
            copy.putSingle(AuthenticationManager.FORM_USERNAME, PhoneNormalizer.normalizeLogin(username));
        }
        return copy;
    }
}
