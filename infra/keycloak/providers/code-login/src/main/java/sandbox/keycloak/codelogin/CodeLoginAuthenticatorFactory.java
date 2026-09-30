package sandbox.keycloak.codelogin;

import java.util.List;

import org.keycloak.Config;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel.Requirement;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Вход по одноразовому коду (SMS или почта). Пока заготовка: проверяем, что Keycloak загружает плагин.
 */
public final class CodeLoginAuthenticatorFactory implements AuthenticatorFactory {

    public static final String PROVIDER_ID = "sandbox-code-login";

    private static final Requirement[] REQUIREMENTS = {Requirement.ALTERNATIVE, Requirement.DISABLED};

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Sandbox: вход по коду (SMS / почта)";
    }

    @Override
    public String getHelpText() {
        return "Одноразовый код на телефон или почту.";
    }

    @Override
    public String getReferenceCategory() {
        return "code-login";
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
        return new CodeLoginAuthenticator();
    }

    @Override
    public void init(Config.Scope config) {
        // настроек пока нет
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
        // зависимостей от других провайдеров пока нет
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
