package platform.keycloak.auth;

import java.time.Clock;

import org.keycloak.Config;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.RequiredActionFactory;
import org.keycloak.authentication.RequiredActionProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;

/** Смена номера самим человеком (см. UpdatePhoneAction). Отправка кодов — с настройками входа по коду. */
public final class UpdatePhoneActionFactory implements RequiredActionFactory {

    public static final String PROVIDER_ID = "platform-update-phone";

    private CodeLoginAuthenticatorFactory codeLogin;

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayText() {
        return "Sandbox: сменить номер телефона";
    }

    @Override
    public RequiredActionProvider create(KeycloakSession session) {
        return new UpdatePhoneAction(codeLogin.dispatcher(), Clock.systemUTC());
    }

    @Override
    public void init(Config.Scope config) {
        // настройки кодов — у входа по коду
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
        codeLogin = (CodeLoginAuthenticatorFactory) factory.getProviderFactory(Authenticator.class,
            CodeLoginAuthenticatorFactory.PROVIDER_ID);
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
