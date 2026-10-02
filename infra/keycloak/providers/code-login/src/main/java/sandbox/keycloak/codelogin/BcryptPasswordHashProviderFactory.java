package sandbox.keycloak.codelogin;

import org.keycloak.Config;
import org.keycloak.credential.hash.PasswordHashProvider;
import org.keycloak.credential.hash.PasswordHashProviderFactory;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;

/** Регистрирует алгоритм «bcrypt» (META-INF/services). */
public final class BcryptPasswordHashProviderFactory implements PasswordHashProviderFactory {

    private static final BcryptPasswordHashProvider PROVIDER = new BcryptPasswordHashProvider();

    @Override
    public PasswordHashProvider create(KeycloakSession session) {
        return PROVIDER;
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
        return BcryptPasswordHashProvider.ID;
    }
}
