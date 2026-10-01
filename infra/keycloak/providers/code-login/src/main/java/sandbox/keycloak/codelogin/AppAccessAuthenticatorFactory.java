package sandbox.keycloak.codelogin;

import java.util.List;

import org.keycloak.Config;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel.Requirement;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.provider.ProviderConfigProperty;

/** Шаг входа «доступ к приложению» (см. AppAccessAuthenticator). */
public final class AppAccessAuthenticatorFactory implements AuthenticatorFactory {

    public static final String PROVIDER_ID = "sandbox-app-access-check";

    private static final Requirement[] REQUIREMENTS = {Requirement.REQUIRED, Requirement.DISABLED};

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Sandbox: доступ к приложению";
    }

    @Override
    public String getHelpText() {
        return "Доступ к приложению закрыт (роль access-denied клиента) — вход в это приложение не пускает.";
    }

    @Override
    public String getReferenceCategory() {
        return null;
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
        return new AppAccessAuthenticator();
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
