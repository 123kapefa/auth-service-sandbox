package platform.keycloak.auth;

import jakarta.ws.rs.core.Response;

import java.util.Optional;
import java.util.Set;

import org.keycloak.OAuthErrorException;
import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.ModelDuplicateException;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.credential.OTPCredentialModel;
import org.keycloak.models.utils.KeycloakModelUtils;
import org.keycloak.representations.AccessToken;
import org.keycloak.representations.idm.ClientPolicyExecutorConfigurationRepresentation;
import org.keycloak.services.clientpolicy.ClientPolicyContext;
import org.keycloak.services.clientpolicy.ClientPolicyException;
import org.keycloak.services.clientpolicy.context.ResourceOwnerPasswordCredentialsContext;
import org.keycloak.services.clientpolicy.context.ResourceOwnerPasswordCredentialsResponseContext;
import org.keycloak.services.clientpolicy.context.TokenRefreshResponseContext;
import org.keycloak.services.clientpolicy.context.TokenResponseContext;
import org.keycloak.services.clientpolicy.executor.ClientPolicyExecutorProvider;

/**
 * Исполнитель политики клиентов: не выдаёт и не обновляет токены приложения тому, у кого закрыт доступ к нему
 * (роль access-denied клиента). Работает для любого приложения realm, а не только для нашего сервиса:
 * refresh-токен уже открытой сессии перестаёт работать, сессии остальных приложений — нет.
 * Ещё — не выдаёт токен по логину и паролю (direct grant), если войти так человеку нельзя: пароль выключен им самим
 * (10.3) или в проекте, вход по номеру или почте выключен в проекте, или 2FA в проекте обязательна, а приложения-
 * аутентификатора нет (direct grant не может его завести). Решение — до проверки пароля и без сессии: ответ один и тот
 * же, верный пароль или нет (401, как на неверный), — утёкший пароль так не проверить.
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
            case RESOURCE_OWNER_PASSWORD_CREDENTIALS_REQUEST -> {
                // токен по логину и паролю (клиенты отладки, например manager-1c-cli): до проверки пароля
                String username = ((ResourceOwnerPasswordCredentialsContext) context).getParams().getFirst("username");
                Optional<UserModel> user = find(username);
                if (user.isPresent() && !passwordGrantAllowed(user.get(), username)) {
                    throw new ClientPolicyException(OAuthErrorException.INVALID_GRANT, "Invalid user credentials",
                        Response.Status.UNAUTHORIZED);
                }
            }
            case RESOURCE_OWNER_PASSWORD_CREDENTIALS_RESPONSE -> {
                ResourceOwnerPasswordCredentialsResponseContext tokens = (ResourceOwnerPasswordCredentialsResponseContext) context;
                if (tokens.getClientSessionContext() != null) {
                    UserModel user = tokens.getClientSessionContext().getClientSession().getUserSession().getUser();
                    check(tokens.getClientSessionContext().getClientSession().getClient(), user);
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

    // Пользователь по логину, как его ищет direct grant (номер — в виде логина); нет или неоднозначно — пусто: тогда
    // ответит сам Keycloak (тот же 401).
    private Optional<UserModel> find(String username) {
        if (username == null || username.isBlank()) {
            return Optional.empty();
        }
        try {
            return Optional.ofNullable(KeycloakModelUtils.findUserByNameOrEmail(session, session.getContext().getRealm(),
                PhoneNormalizer.normalizeLogin(username)));
        } catch (ModelDuplicateException e) {
            return Optional.empty();
        }
    }

    // Те же правила, что у страницы входа (PasswordLoginForm, AppAccessAuthenticator).
    private boolean passwordGrantAllowed(UserModel user, String typed) {
        RealmModel realm = session.getContext().getRealm();
        if (SignInMethods.isDisabled(session, realm, user, SignInMethods.PASSWORD)) {
            return false;
        }
        Set<ProjectSignIn.Method> blocked = ProjectSignInRealm.blocked(session, realm, user);
        // Вход тем, что ввели (номер, почта), выключен в проекте — как loginOff страницы входа.
        boolean loginOff = CodeTarget.parse(PhoneNormalizer.normalizeLogin(typed))
            .map(target -> blocked.contains(ProjectSignInRealm.method(target.channel())))
            .orElse(false);
        if (loginOff) {
            return false;
        }
        return !ProjectSignInRealm.settings(session, realm).twoFactorRequired()
            || user.credentialManager().getStoredCredentialsByTypeStream(OTPCredentialModel.TYPE).findAny().isPresent();
    }

    private static void check(ClientModel client, UserModel user) throws ClientPolicyException {
        if (AppAccess.isDenied(client, user)) {
            throw new ClientPolicyException(OAuthErrorException.INVALID_GRANT, "Access to the application is closed");
        }
    }
}
