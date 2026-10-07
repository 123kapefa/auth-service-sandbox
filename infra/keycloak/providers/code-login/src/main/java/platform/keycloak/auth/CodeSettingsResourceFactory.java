package platform.keycloak.auth;

import java.time.Clock;

import org.keycloak.Config;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.services.resource.RealmResourceProvider;
import org.keycloak.services.resource.RealmResourceProviderFactory;

/** Коды проекта для админки: /realms/{realm}/platform-codes (CodeSettingsResource). */
public final class CodeSettingsResourceFactory implements RealmResourceProviderFactory {

    public static final String PROVIDER_ID = "platform-codes";

    @Override
    public RealmResourceProvider create(KeycloakSession session) {
        return new CodeSettingsResource(session, Clock.systemUTC());
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
