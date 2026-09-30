package sandbox.keycloak.codelogin;

import jakarta.ws.rs.core.MultivaluedHashMap;
import jakarta.ws.rs.core.MultivaluedMap;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.authenticators.browser.UsernamePasswordForm;
import org.keycloak.models.KeycloakSession;
import org.keycloak.services.managers.AuthenticationManager;

/**
 * Обычная форма «Телефон или почта + пароль» (встроенный UsernamePasswordForm), но номер приводится к логину
 * на сервере: «+7 (999) 000-00-01», «8 999 000 00 01» и «9990000001» — один пользователь 79990000001.
 * Скрипт темы только показывает «+»; запрос в обход формы тоже найдёт пользователя.
 */
public final class PasswordLoginForm extends UsernamePasswordForm {

    public PasswordLoginForm(KeycloakSession session) {
        super(session);
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
