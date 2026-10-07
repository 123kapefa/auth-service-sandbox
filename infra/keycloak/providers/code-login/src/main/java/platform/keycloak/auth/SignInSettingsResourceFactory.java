package platform.keycloak.auth;

import java.time.Clock;

import org.keycloak.Config;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.services.resource.RealmResourceProvider;
import org.keycloak.services.resource.RealmResourceProviderFactory;

/** Способы входа проекта для админки: /realms/{realm}/platform-sign-in (SignInSettingsResource). */
public final class SignInSettingsResourceFactory implements RealmResourceProviderFactory {

    public static final String PROVIDER_ID = "platform-sign-in";

    @Override
    public RealmResourceProvider create(KeycloakSession session) {
        return new SignInSettingsResource(session, Clock.systemUTC());
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
