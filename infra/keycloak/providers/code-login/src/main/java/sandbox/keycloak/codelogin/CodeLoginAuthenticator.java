package sandbox.keycloak.codelogin;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;

import jakarta.ws.rs.core.MultivaluedMap;
import jakarta.ws.rs.core.Response;

import org.jboss.logging.Logger;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.authentication.Authenticator;
import org.keycloak.events.Details;
import org.keycloak.events.Errors;
import org.keycloak.events.EventType;
import org.keycloak.forms.login.LoginFormsProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.services.messages.Messages;

/**
 * Вход по одноразовому коду. Номер уже выбран на форме входа (заметка CodeLoginNotes.TARGET):
 * здесь — отправка кода, экран «Введите код», проверка и вход. Правила кода — CodeRules (без Keycloak).
 * Неизвестный номер: пользователь создаётся только после верного кода — как в боевом сервисе.
 */
public final class CodeLoginAuthenticator implements Authenticator {

    static final String FORM = "sandbox-code-login.ftl";
    static final String FIELD_CODE = "code";
    static final String BUTTON_RESEND = "resend";

    private static final Logger LOG = Logger.getLogger(CodeLoginAuthenticator.class);
    private static final String CUSTOMER_MODE = "customer";

    private final CodeRules rules;
    private final CodeSender sender;
    private final Clock clock;

    CodeLoginAuthenticator(CodeRules rules, CodeSender sender, Clock clock) {
        this.rules = rules;
        this.sender = sender;
        this.clock = clock;
    }

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        String target = target(context);
        if (target == null) {
            // Сюда попадают только через условие «выбран вход по коду»; без номера делать нечего.
            context.attempted();
            return;
        }
        send(context, target);
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        String target = target(context);
        if (target == null) {
            context.attempted();
            return;
        }

        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        if (formData.containsKey(BUTTON_RESEND)) {
            send(context, target);
            return;
        }

        CodeStore store = new CodeStore(context.getSession(), context.getRealm());
        CodeState current = store.load(target);
        context.getEvent().detail(Details.USERNAME, target);

        switch (rules.verify(current, formData.getFirst(FIELD_CODE))) {
            case CodeRules.VerifyResult.Success success -> {
                store.save(target, success.state(), clock.instant());
                signIn(context, target, success.state());
            }
            case CodeRules.VerifyResult.Wrong wrong -> {
                store.save(target, wrong.state(), clock.instant());
                fail(context, target, wrong.state(), "sandboxCodeWrong", wrong.attemptsLeft());
            }
            case CodeRules.VerifyResult.LockedNow locked -> {
                store.save(target, locked.state(), clock.instant());
                fail(context, target, locked.state(), "sandboxCodeLocked", locked.retryAfterSeconds());
            }
            case CodeRules.VerifyResult.Locked locked ->
                fail(context, target, current, "sandboxCodeLocked", locked.retryAfterSeconds());
            case CodeRules.VerifyResult.NoActiveCode ignored ->
                fail(context, target, current, "sandboxCodeExpired");
        }
    }

    private void send(AuthenticationFlowContext context, String target) {
        CodeStore store = new CodeStore(context.getSession(), context.getRealm());
        CodeState current = store.load(target);

        switch (rules.send(current)) {
            case CodeRules.SendResult.Sent sent -> {
                store.save(target, sent.state(), clock.instant());
                sender.send(target, sent.code());
                context.challenge(form(context, target, sent.state()).createForm(FORM));
            }
            case CodeRules.SendResult.TooEarly early -> context.challenge(
                form(context, target, current).setInfo("sandboxCodeTooEarly", early.retryAfterSeconds()).createForm(FORM));
            case CodeRules.SendResult.Locked locked -> context.challenge(
                form(context, target, current).setError("sandboxCodeLocked", locked.retryAfterSeconds()).createForm(FORM));
        }
    }

    private void signIn(AuthenticationFlowContext context, String target, CodeState state) {
        KeycloakSession session = context.getSession();
        RealmModel realm = context.getRealm();

        UserModel user = session.users().getUserByUsername(realm, target);
        if (user == null) {
            user = createCustomer(context, target);
        }

        if (!user.isEnabled()) {
            context.getEvent().user(user);
            context.getEvent().error(Errors.USER_DISABLED);
            Response challenge = form(context, target, state).setError(Messages.ACCOUNT_DISABLED).createForm(FORM);
            context.failureChallenge(AuthenticationFlowError.USER_DISABLED, challenge);
            return;
        }

        context.setUser(user);
        context.getEvent().detail(Details.AUTH_METHOD, "sms-code");
        context.success();
    }

    // Новый покупатель: логин — номер цифрами, роль customer — из ролей по умолчанию realm.
    private static UserModel createCustomer(AuthenticationFlowContext context, String target) {
        UserModel user = context.getSession().users().addUser(context.getRealm(), target);
        user.setEnabled(true);
        user.setSingleAttribute("phone", PhoneNormalizer.toE164(target));
        user.setSingleAttribute("active_mode", CUSTOMER_MODE);

        context.getEvent().clone()
            .event(EventType.REGISTER)
            .user(user)
            .detail(Details.USERNAME, target)
            .detail(Details.REGISTER_METHOD, "sms-code")
            .success();
        LOG.infof("Новый покупатель по коду: %s", target);
        return user;
    }

    private void fail(AuthenticationFlowContext context, String target, CodeState state, String message, Object... parameters) {
        context.getEvent().error(Errors.INVALID_USER_CREDENTIALS);
        Response challenge = form(context, target, state).setError(message, parameters).createForm(FORM);
        context.failureChallenge(AuthenticationFlowError.INVALID_CREDENTIALS, challenge);
    }

    private LoginFormsProvider form(AuthenticationFlowContext context, String target, CodeState state) {
        Instant now = clock.instant();
        long resendAfter = state.resendAllowedAt() == null || !now.isBefore(state.resendAllowedAt())
            ? 0
            : (Duration.between(now, state.resendAllowedAt()).toMillis() + 999) / 1000;

        return context.form()
            .setExecution(context.getExecution().getId())
            .setAttribute("codeTarget", PhoneNormalizer.toE164(target))
            .setAttribute("codeResendAfter", Long.toString(resendAfter));
    }

    private static String target(AuthenticationFlowContext context) {
        return context.getAuthenticationSession().getAuthNote(CodeLoginNotes.TARGET);
    }

    @Override
    public boolean requiresUser() {
        return false;
    }

    @Override
    public boolean configuredFor(KeycloakSession session, RealmModel realm, UserModel user) {
        return true;
    }

    @Override
    public void setRequiredActions(KeycloakSession session, RealmModel realm, UserModel user) {
        // обязательных действий нет
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
