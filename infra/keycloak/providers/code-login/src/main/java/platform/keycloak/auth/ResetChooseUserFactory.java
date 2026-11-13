package platform.keycloak.auth;

import java.util.List;

import org.keycloak.Config;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel.Requirement;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.provider.ProviderConfigProperty;

/** «Забыли пароль?»: выбор пользователя с приведением номера к логину на сервере (см. ResetChooseUser). */
public final class ResetChooseUserFactory implements AuthenticatorFactory {

    public static final String PROVIDER_ID = "platform-reset-choose-user";

    private static final Requirement[] REQUIREMENTS = {Requirement.REQUIRED};

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Sandbox: сброс пароля — телефон или почта";
    }

    @Override
    public String getHelpText() {
        return "Выбор пользователя при сбросе пароля; номер телефона приводится к логину на сервере.";
    }

    @Override
    public String getReferenceCategory() {
        // Как у встроенного ResetCredentialChooseUser (без категории).
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
        return new ResetChooseUser();
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
