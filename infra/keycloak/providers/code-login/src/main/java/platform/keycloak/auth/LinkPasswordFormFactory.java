package platform.keycloak.auth;

import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.authenticators.broker.IdpUsernamePasswordFormFactory;
import org.keycloak.models.KeycloakSession;

/** Шаг «пароль от существующей учётной записи» при привязке внешнего входа (сценарий platform link existing). */
public final class LinkPasswordFormFactory extends IdpUsernamePasswordFormFactory {

    public static final String PROVIDER_ID = "platform-link-password";

    @Override
    public Authenticator create(KeycloakSession session) {
        return new LinkPasswordForm(session);
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Platform: пароль от существующей учётной записи (привязка внешнего входа)";
    }

    @Override
    public String getHelpText() {
        return "Учётная запись — та, с которой совпал человек из Госуслуг или Яндекс ID; спрашивается только пароль";
    }
}
