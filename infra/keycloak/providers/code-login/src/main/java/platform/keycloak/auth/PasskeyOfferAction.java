package platform.keycloak.auth;

import java.net.URI;
import java.time.Clock;

import jakarta.ws.rs.core.Response;
import jakarta.ws.rs.core.UriInfo;

import org.keycloak.authentication.RequiredActionContext;
import org.keycloak.authentication.RequiredActionProvider;
import org.keycloak.common.ClientConnection;
import org.keycloak.events.EventBuilder;
import org.keycloak.forms.login.LoginFormsProvider;
import org.keycloak.http.HttpRequest;
import org.keycloak.models.Constants;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RequiredActionConfigModel;
import org.keycloak.models.RequiredActionProviderModel;
import org.keycloak.models.UserModel;
import org.keycloak.sessions.AuthenticationSessionModel;

/**
 * «Входите без кода» (10.6): после входа по коду — предложить завести ключ доступа, чтобы в следующий раз войти без
 * кода. Только в этом входе (не в аккаунте человека), можно пропустить; после «Не сейчас» не спрашиваем 30 дней
 * (PasskeyOfferRules). Вход паролем, ключом, внешний, по куке и действия из кабинета (kc_action) — без предложения.
 * <p>
 * Регистрацию ключа делает стандартное действие Keycloak webauthn-register-passwordless: его страницы
 * (webauthn-register.ftl, webauthn-error.ftl) получают атрибут platformPasskeyOffer, и тема рисует вместо них экран
 * предложения с «Не сейчас» (кнопка skip).
 */
public final class PasskeyOfferAction implements RequiredActionProvider {

    /** Заметка входа: вошли по коду (ставит CodeLoginAuthenticator). */
    static final String NOTE_CODE_LOGIN = "platform.passkey-offer.code-login";
    /**
     * Заметка входа: человек вошёл способом в переходном периоде проекта (ProjectSignIn) — «телефон:2026-11-05».
     * Предлагаем при каждом входе, без паузы после «Не сейчас»; на странице — атрибут platformSignInTransition.
     */
    static final String NOTE_TRANSITION = "platform.passkey-offer.transition";
    static final String ATTRIBUTE_TRANSITION = "platformSignInTransition";
    /** Заметка входа: в этом входе уже предложили (триггеры проверяются после каждого действия — не по кругу). */
    private static final String NOTE_OFFERED = "platform.passkey-offer.offered";
    /** Атрибут страницы: это предложение, а не регистрация ключа из кабинета. */
    static final String ATTRIBUTE = "platformPasskeyOffer";
    static final String BUTTON_SKIP = "skip";

    private static final String PASSKEY = "webauthn-passwordless";
    private static final String REGISTER_PASSKEY = "webauthn-register-passwordless";

    private final Clock clock;

    PasskeyOfferAction(Clock clock) {
        this.clock = clock;
    }

    @Override
    public void evaluateTriggers(RequiredActionContext context) {
        AuthenticationSessionModel auth = context.getAuthenticationSession();
        boolean transition = auth.getAuthNote(NOTE_TRANSITION) != null && auth.getAuthNote(NOTE_OFFERED) == null;
        if (auth.getAuthNote(NOTE_CODE_LOGIN) == null && !transition) {
            return;
        }
        auth.removeAuthNote(NOTE_CODE_LOGIN);
        if (auth.getClientNote(Constants.KC_ACTION) != null) {
            return;
        }
        UserModel user = context.getUser();
        boolean hasPasskey = user.credentialManager().getStoredCredentialsByTypeStream(PASSKEY).findAny().isPresent();
        if (transition && passkeysEnabled(context.getRealm()) && !hasPasskey) {
            auth.setAuthNote(NOTE_OFFERED, "true");
            auth.addRequiredAction(PasskeyOfferActionFactory.PROVIDER_ID);
            return;
        }
        if (PasskeyOfferRules.shouldOffer(passkeysEnabled(context.getRealm()), hasPasskey,
                user.getFirstAttribute(PasskeyOfferRules.SKIPPED_AT), clock.instant())) {
            auth.addRequiredAction(PasskeyOfferActionFactory.PROVIDER_ID);
        }
    }

    @Override
    public void requiredActionChallenge(RequiredActionContext context) {
        RequiredActionProvider register = register(context.getSession());
        if (register == null) {
            context.success();
            return;
        }
        register.requiredActionChallenge(new OfferContext(context));
    }

    @Override
    public void processAction(RequiredActionContext context) {
        if (context.getHttpRequest().getDecodedFormParameters().containsKey(BUTTON_SKIP)) {
            context.getUser().setSingleAttribute(PasskeyOfferRules.SKIPPED_AT, clock.instant().toString());
            context.success();
            return;
        }
        RequiredActionProvider register = register(context.getSession());
        if (register == null) {
            context.success();
            return;
        }
        register.processAction(new OfferContext(context));
    }

    // Ключи доступа включены в realm, и регистрация ключа — тоже.
    private static boolean passkeysEnabled(RealmModel realm) {
        RequiredActionProviderModel register = realm.getRequiredActionProviderByAlias(REGISTER_PASSKEY);
        return realm.getWebAuthnPolicyPasswordless().isPasskeysEnabled() && register != null && register.isEnabled();
    }

    private static RequiredActionProvider register(KeycloakSession session) {
        return session.getProvider(RequiredActionProvider.class, REGISTER_PASSKEY);
    }

    @Override
    public void close() {
        // ресурсов нет
    }

    /** Контекст для регистрации ключа: всё как есть, только у страниц — атрибут platformPasskeyOffer. */
    private record OfferContext(RequiredActionContext context) implements RequiredActionContext {

        @Override
        public LoginFormsProvider form() {
            LoginFormsProvider form = context.form().setAttribute(ATTRIBUTE, "true");
            String transition = context.getAuthenticationSession().getAuthNote(NOTE_TRANSITION);
            return transition == null ? form : form.setAttribute(ATTRIBUTE_TRANSITION, transition);
        }

        @Override
        public String getAction() {
            return context.getAction();
        }

        @Override
        public URI getActionUrl(String code) {
            return context.getActionUrl(code);
        }

        @Override
        public URI getActionUrl() {
            return context.getActionUrl();
        }

        @Override
        public Response getChallenge() {
            return context.getChallenge();
        }

        @Override
        public EventBuilder getEvent() {
            return context.getEvent();
        }

        @Override
        public UserModel getUser() {
            return context.getUser();
        }

        @Override
        public RealmModel getRealm() {
            return context.getRealm();
        }

        @Override
        public AuthenticationSessionModel getAuthenticationSession() {
            return context.getAuthenticationSession();
        }

        @Override
        public ClientConnection getConnection() {
            return context.getConnection();
        }

        @Override
        public UriInfo getUriInfo() {
            return context.getUriInfo();
        }

        @Override
        public KeycloakSession getSession() {
            return context.getSession();
        }

        @Override
        public HttpRequest getHttpRequest() {
            return context.getHttpRequest();
        }

        @Override
        public RequiredActionConfigModel getConfig() {
            return context.getConfig();
        }

        @Override
        public String generateCode() {
            return context.generateCode();
        }

        @Override
        public Status getStatus() {
            return context.getStatus();
        }

        @Override
        public String getErrorMessage() {
            return context.getErrorMessage();
        }

        @Override
        public void challenge(Response response) {
            context.challenge(response);
        }

        @Override
        public void failure(String errorMessage) {
            context.failure(errorMessage);
        }

        @Override
        public void success() {
            context.success();
        }

        @Override
        public void cancel() {
            context.cancel();
        }

        @Override
        public void ignore() {
            context.ignore();
        }
    }
}
