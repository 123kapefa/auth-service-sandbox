package platform.keycloak.auth;

import java.util.List;

import org.keycloak.Config;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel.Requirement;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.models.credential.PasswordCredentialModel;
import org.keycloak.provider.ProviderConfigProperty;

/** Форма «Телефон или почта + пароль» с приведением номера к логину на сервере (см. PasswordLoginForm). */
public final class PasswordLoginFormFactory implements AuthenticatorFactory {

    public static final String PROVIDER_ID = "platform-password-login";

    private static final Requirement[] REQUIREMENTS = {Requirement.REQUIRED, Requirement.ALTERNATIVE};

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Sandbox: телефон или почта + пароль";
    }

    @Override
    public String getHelpText() {
        return "Стандартная форма логина и пароля; номер телефона приводится к логину на сервере.";
    }

    @Override
    public String getReferenceCategory() {
        // Как у встроенного UsernamePasswordForm.
        return PasswordCredentialModel.TYPE;
    }

    @Override
    public Requirement[] getRequirementChoices() {
        return REQUIREMENTS;
    }

    @Override
    public boolean isConfigurable() {
        return false;
    }

    @Override
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return List.of();
    }

    @Override
    public Authenticator create(KeycloakSession session) {
        return new PasswordLoginForm(session);
    }

    @Override
    public void init(Config.Scope config) {
        // настроек нет
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
        // зависимостей от других провайдеров нет
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
