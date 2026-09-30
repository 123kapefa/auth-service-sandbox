package sandbox.keycloak.codelogin;

import java.util.List;

import org.keycloak.Config;
import org.keycloak.authentication.authenticators.conditional.ConditionalAuthenticator;
import org.keycloak.authentication.authenticators.conditional.ConditionalAuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel.Requirement;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.provider.ProviderConfigProperty;

public final class CodeRequestedConditionFactory implements ConditionalAuthenticatorFactory {

    public static final String PROVIDER_ID = "sandbox-condition-code-requested";

    private static final Requirement[] REQUIREMENTS = {Requirement.REQUIRED, Requirement.DISABLED};

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Sandbox: условие — выбран вход по коду";
    }

    @Override
    public String getHelpText() {
        return "Выполнять подсценарий, только если на форме входа нажали «Войти по коду».";
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
    public ConditionalAuthenticator getSingleton() {
        return CodeRequestedCondition.SINGLETON;
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
