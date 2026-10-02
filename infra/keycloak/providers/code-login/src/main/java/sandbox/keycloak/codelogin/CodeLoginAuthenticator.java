package sandbox.keycloak.codelogin;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Map;
import java.util.Optional;

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
 * Вход по одноразовому коду. Куда слать код, уже выбрано на форме входа (CodeLoginNotes): номер — SMS, почта — письмо.
 * Здесь — отправка кода, экран «Введите код», проверка и вход. Правила кода — CodeRules (без Keycloak).
 * Неизвестный номер или почта: пользователь создаётся только после верного кода — как в боевом сервисе.
 */
public final class CodeLoginAuthenticator implements Authenticator {

    static final String FORM = "sandbox-code-login.ftl";
    static final String FIELD_CODE = "code";
    static final String BUTTON_RESEND = "resend";

    private static final Logger LOG = Logger.getLogger(CodeLoginAuthenticator.class);
    private static final String CUSTOMER_MODE = "customer";

    private final CodeRules rules;
    private final Map<CodeTarget.Channel, CodeSender> senders;
    private final Clock clock;

    CodeLoginAuthenticator(CodeRules rules, Map<CodeTarget.Channel, CodeSender> senders, Clock clock) {
        this.rules = rules;
        this.senders = senders;
        this.clock = clock;
    }

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        Optional<CodeTarget> target = CodeLoginNotes.load(context.getAuthenticationSession());
        if (target.isEmpty()) {
            // Сюда попадают только через условие «выбран вход по коду»; без адресата делать нечего.
            context.attempted();
            return;
        }
        send(context, target.get());
    }

    @Override
    public void action(AuthenticationFlowContext context) {
        Optional<CodeTarget> loaded = CodeLoginNotes.load(context.getAuthenticationSession());
        if (loaded.isEmpty()) {
            context.attempted();
            return;
        }
        CodeTarget target = loaded.get();

        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        if (formData.containsKey(BUTTON_RESEND)) {
            send(context, target);
            return;
        }

        CodeStore store = new CodeStore(context.getSession(), context.getRealm());
        CodeState current = store.load(target);
        context.getEvent().detail(Details.USERNAME, target.value());

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

    private void send(AuthenticationFlowContext context, CodeTarget target) {
        CodeStore store = new CodeStore(context.getSession(), context.getRealm());
        CodeState current = store.load(target);

        switch (rules.send(current)) {
            case CodeRules.SendResult.Sent sent -> {
                // Сохраняем только отправленный код: иначе пауза перед повтором началась бы без письма.
                if (!senders.get(target.channel()).send(context, target.value(), sent.code())) {
                    context.challenge(form(context, target, current).setError("sandboxCodeSendFailed").createForm(FORM));
                    return;
                }
                store.save(target, sent.state(), clock.instant());
                context.challenge(form(context, target, sent.state()).createForm(FORM));
            }
            case CodeRules.SendResult.TooEarly early -> context.challenge(
                form(context, target, current).setInfo("sandboxCodeTooEarly", early.retryAfterSeconds()).createForm(FORM));
            case CodeRules.SendResult.Locked locked -> context.challenge(
                form(context, target, current).setError("sandboxCodeLocked", locked.retryAfterSeconds()).createForm(FORM));
        }
    }

    private void signIn(AuthenticationFlowContext context, CodeTarget target, CodeState state) {
        UserModel user = findUser(context.getSession(), context.getRealm(), target);
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
        context.getEvent().detail(Details.AUTH_METHOD, method(target));
        context.success();
    }

    private static UserModel findUser(KeycloakSession session, RealmModel realm, CodeTarget target) {
        return target.channel() == CodeTarget.Channel.SMS
            ? session.users().getUserByUsername(realm, target.value())
            : session.users().getUserByEmail(realm, target.value());
    }

    // Новый покупатель: логин — номер цифрами или почта; роль customer — из ролей по умолчанию realm.
    private static UserModel createCustomer(AuthenticationFlowContext context, CodeTarget target) {
        UserModel user = context.getSession().users().addUser(context.getRealm(), target.value());
        user.setEnabled(true);
        if (target.channel() == CodeTarget.Channel.SMS) {
            user.setSingleAttribute("phone", target.display());
        } else {
            // Код из письма и есть подтверждение адреса.
            user.setEmail(target.value());
            user.setEmailVerified(true);
        }
        user.setSingleAttribute("active_mode", CUSTOMER_MODE);

        context.getEvent().clone()
            .event(EventType.REGISTER)
            .user(user)
            .detail(Details.USERNAME, target.value())
            .detail(Details.REGISTER_METHOD, method(target))
            .success();
        LOG.infof("Новый покупатель по коду: %s", target.value());
        return user;
    }

    private static String method(CodeTarget target) {
        return target.channel() == CodeTarget.Channel.SMS ? "sms-code" : "email-code";
    }

    private void fail(AuthenticationFlowContext context, CodeTarget target, CodeState state, String message, Object... parameters) {
        context.getEvent().error(Errors.INVALID_USER_CREDENTIALS);
        Response challenge = form(context, target, state).setError(message, parameters).createForm(FORM);
        context.failureChallenge(AuthenticationFlowError.INVALID_CREDENTIALS, challenge);
    }

    private LoginFormsProvider form(AuthenticationFlowContext context, CodeTarget target, CodeState state) {
        Instant now = clock.instant();
        long resendAfter = state.resendAllowedAt() == null || !now.isBefore(state.resendAllowedAt())
            ? 0
            : (Duration.between(now, state.resendAllowedAt()).toMillis() + 999) / 1000;

        return context.form()
            .setExecution(context.getExecution().getId())
            .setAttribute("codeChannel", target.channel().name())
            .setAttribute("codeTarget", target.display())
            .setAttribute("codeResendAfter", Long.toString(resendAfter))
            .setAttribute("codeLength", Integer.toString(rules.codeLength()));
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
