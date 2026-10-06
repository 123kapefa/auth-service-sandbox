package platform.keycloak.auth;

import java.time.Clock;

import org.keycloak.Config;
import org.keycloak.authentication.RequiredActionFactory;
import org.keycloak.authentication.RequiredActionProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;

/** «Входите без кода» — предложение ключа доступа после входа по коду (см. PasskeyOfferAction). */
public final class PasskeyOfferActionFactory implements RequiredActionFactory {

    public static final String PROVIDER_ID = "platform-passkey-offer";

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayText() {
        return "Sandbox: предложить ключ доступа после входа по коду";
    }

    @Override
    public RequiredActionProvider create(KeycloakSession session) {
        return new PasskeyOfferAction(Clock.systemUTC());
    }

    @Override
    public void init(Config.Scope config) {
        // настроек нет
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
        // нечего
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
