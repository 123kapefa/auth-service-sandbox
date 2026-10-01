package sandbox.keycloak.codelogin;

import java.util.List;

import org.keycloak.Config;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.services.clientpolicy.executor.ClientPolicyExecutorProvider;
import org.keycloak.services.clientpolicy.executor.ClientPolicyExecutorProviderFactory;

/** Исполнитель «доступ к приложению» (см. AppAccessExecutor). Подключается профилем и политикой клиентов в realm. */
public final class AppAccessExecutorFactory implements ClientPolicyExecutorProviderFactory {

    public static final String PROVIDER_ID = "sandbox-app-access";

    @Override
    public ClientPolicyExecutorProvider create(KeycloakSession session) {
        return new AppAccessExecutor(session);
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getHelpText() {
        return "Не выдаёт и не обновляет токены приложения пользователю с ролью access-denied этого клиента.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return List.of();
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
