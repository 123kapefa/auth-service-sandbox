package sandbox.keycloak.codelogin;

import org.keycloak.OAuthErrorException;
import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.UserModel;
import org.keycloak.representations.AccessToken;
import org.keycloak.representations.idm.ClientPolicyExecutorConfigurationRepresentation;
import org.keycloak.services.clientpolicy.ClientPolicyContext;
import org.keycloak.services.clientpolicy.ClientPolicyException;
import org.keycloak.services.clientpolicy.context.ResourceOwnerPasswordCredentialsResponseContext;
import org.keycloak.services.clientpolicy.context.TokenRefreshResponseContext;
import org.keycloak.services.clientpolicy.context.TokenResponseContext;
import org.keycloak.services.clientpolicy.executor.ClientPolicyExecutorProvider;

/**
 * Исполнитель политики клиентов: не выдаёт и не обновляет токены приложения тому, у кого закрыт доступ к нему
 * (роль access-denied клиента). Работает для любого приложения realm, а не только для нашего сервиса:
 * refresh-токен уже открытой сессии перестаёт работать, сессии остальных приложений — нет.
 */
public final class AppAccessExecutor implements ClientPolicyExecutorProvider<ClientPolicyExecutorConfigurationRepresentation> {

    private final KeycloakSession session;

    public AppAccessExecutor(KeycloakSession session) {
        this.session = session;
    }

    @Override
    public String getProviderId() {
        return AppAccessExecutorFactory.PROVIDER_ID;
    }

    @Override
    public void executeOnEvent(ClientPolicyContext context) throws ClientPolicyException {
        switch (context.getEvent()) {
            case TOKEN_RESPONSE -> {
                TokenResponseContext tokens = (TokenResponseContext) context;
                if (tokens.getClientSessionContext() != null) {
                    check(tokens.getClientSessionContext().getClientSession().getClient(),
                        tokens.getClientSessionContext().getClientSession().getUserSession().getUser());
                }
            }
            case RESOURCE_OWNER_PASSWORD_CREDENTIALS_RESPONSE -> {
                // токен по логину и паролю (клиенты отладки, например manager-1c-cli)
                ResourceOwnerPasswordCredentialsResponseContext tokens = (ResourceOwnerPasswordCredentialsResponseContext) context;
                if (tokens.getClientSessionContext() != null) {
                    check(tokens.getClientSessionContext().getClientSession().getClient(),
                        tokens.getClientSessionContext().getClientSession().getUserSession().getUser());
                }
            }
            case TOKEN_REFRESH_RESPONSE -> {
                AccessToken token = ((TokenRefreshResponseContext) context).getAccessTokenResponseBuilder().getAccessToken();
                if (token != null && token.getSubject() != null) {
                    ClientModel client = session.getContext().getClient();
                    check(client, session.users().getUserById(session.getContext().getRealm(), token.getSubject()));
                }
            }
            default -> {
                // остальные события не касаются выдачи токенов пользователю
            }
        }
    }

    private static void check(ClientModel client, UserModel user) throws ClientPolicyException {
        if (AppAccess.isDenied(client, user)) {
            throw new ClientPolicyException(OAuthErrorException.INVALID_GRANT, "Access to the application is closed");
        }
    }
}
