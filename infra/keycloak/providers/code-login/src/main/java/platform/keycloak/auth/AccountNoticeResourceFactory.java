package platform.keycloak.auth;

import org.keycloak.Config;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.services.resource.RealmResourceProvider;
import org.keycloak.services.resource.RealmResourceProviderFactory;

/** Уведомления человеку об изменениях в аккаунте: /realms/{realm}/platform-account-notice (AccountNoticeResource). */
public final class AccountNoticeResourceFactory implements RealmResourceProviderFactory {

    public static final String PROVIDER_ID = "platform-account-notice";

    @Override
    public RealmResourceProvider create(KeycloakSession session) {
        return new AccountNoticeResource(session);
    }

    @Override
    public void init(Config.Scope config) {
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }
}
