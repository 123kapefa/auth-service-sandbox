package platform.keycloak.auth;

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
 * Вход по одноразовому коду. Куда слать код, уже выбрано на форме входа (CodeLoginNotes): номер — звонок или SMS,
 * почта — письмо. Адрес из аккаунта (вход в два шага) на экране скрыт частично.
 * Здесь — отправка кода, экран «Введите код», проверка и вход. Правила кода — CodeRules (без Keycloak).
 * Неизвестный номер или почта: пользователь создаётся только после верного кода — как в боевом сервисе.
 * Код на телефон или почту выключен у человека (SignInMethods) — код не уходит, экран тот же.
 */
public final class CodeLoginAuthenticator implements Authenticator {

    static final String FORM = "platform-code-login.ftl";
    static final String FIELD_CODE = "code";
    static final String BUTTON_RESEND = "resend";
    /** «Прислать SMS» — вместо звонка (звонок не дошёл). */
    static final String BUTTON_SEND_SMS = "sendSms";

    private static final Logger LOG = Logger.getLogger(CodeLoginAuthenticator.class);
    private static final String CUSTOMER_MODE = "customer";

    private final CodeDispatcher codes;
    private final CodeSender email;

    CodeLoginAuthenticator(CodeDispatcher codes, CodeSender email) {
        this.codes = codes;
        this.email = email;
    }

    @Override
    public void authenticate(AuthenticationFlowContext context) {
        Optional<CodeTarget> target = CodeLoginNotes.load(context.getAuthenticationSession());
        if (target.isEmpty()) {
            // Сюда попадают только через условие «выбран вход по коду»; без адресата делать нечего.
            context.attempted();
            return;
        }
        send(context, target.get(), false);
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
        if (formData.containsKey(BUTTON_RESEND) || formData.containsKey(BUTTON_SEND_SMS)) {
            send(context, target, formData.containsKey(BUTTON_SEND_SMS));
            return;
        }

        CodeStore store = new CodeStore(context.getSession(), context.getRealm());
        CodeState current = store.load(target);
        context.getEvent().detail(Details.USERNAME, target.value());

        switch (codes.verify(store, target, formData.getFirst(FIELD_CODE))) {
            case CodeRules.VerifyResult.Success success -> {
                codes.metrics().verified(context.getRealm().getName(), CodeDispatcher.delivery(target, current));
                signIn(context, target, success.state());
            }
            case CodeRules.VerifyResult.Wrong wrong ->
                fail(context, target, wrong.state(), "platformCodeWrong", wrong.attemptsLeft());
            case CodeRules.VerifyResult.LockedNow locked ->
                fail(context, target, locked.state(), "platformCodeLocked", locked.retryAfterSeconds());
            case CodeRules.VerifyResult.Locked locked ->
                fail(context, target, current, "platformCodeLocked", locked.retryAfterSeconds());
            case CodeRules.VerifyResult.NoActiveCode ignored ->
                fail(context, target, current, "platformCodeExpired");
        }
    }

    private void send(AuthenticationFlowContext context, CodeTarget target, boolean smsRequested) {
        CodeDispatcher.Request request = new CodeDispatcher.Request(context.getSession(), context.getRealm(),
            new CodeStore(context.getSession(), context.getRealm()), target, context.getConnection().getRemoteAddr(),
            context.getHttpRequest().getDecodedFormParameters().getFirst(CaptchaVerifier.FIELD));
        CodeDispatcher.Outcome outcome = codes.send(request, smsRequested, methodDisabled(context, target),
            (to, code) -> email.send(context, to, code));
        LoginFormsProvider form = form(context, target, outcome.state());
        switch (outcome) {
            case CodeDispatcher.Outcome.Sent ignored -> {
                // экран кода без сообщения
            }
            case CodeDispatcher.Outcome.TooEarly early -> form.setInfo("platformCodeTooEarly", early.retryAfter());
            case CodeDispatcher.Outcome.Locked locked -> form.setError("platformCodeLocked", locked.retryAfter());
            case CodeDispatcher.Outcome.Refused refused -> {
                if (refused.captcha()) {
                    form.setAttribute("captchaRequired", "true");
                }
                form.setError(refused.message(), refused.parameters());
            }
            case CodeDispatcher.Outcome.Failed ignored -> form.setError("platformCodeSendFailed");
        }
        context.challenge(form.createForm(FORM));
    }

    private static boolean methodDisabled(AuthenticationFlowContext context, CodeTarget target) {
        UserModel user = findUser(context.getSession(), context.getRealm(), target);
        return user != null && SignInMethods.isDisabled(context.getSession(), context.getRealm(), user, signInMethod(target.channel()));
    }

    private static String signInMethod(CodeTarget.Channel channel) {
        return LoginSteps.codeMethod(channel);
    }

    private void signIn(AuthenticationFlowContext context, CodeTarget target, CodeState state) {
        UserModel user = findUser(context.getSession(), context.getRealm(), target);
        if (user == null) {
            user = createCustomer(context, target);
        }

        // Способ выключили, пока человек вводил код, — код больше не годится.
        if (SignInMethods.isDisabled(context.getSession(), context.getRealm(), user, signInMethod(target.channel()))) {
            context.getEvent().user(user).detail(SignInMethods.EVENT_REASON, "method_disabled");
            fail(context, target, state, "platformCodeExpired");
            return;
        }

        if (!user.isEnabled()) {
            context.getEvent().user(user);
            context.getEvent().error(Errors.USER_DISABLED);
            Response challenge = form(context, target, state).setError(Messages.ACCOUNT_DISABLED).createForm(FORM);
            context.failureChallenge(AuthenticationFlowError.USER_DISABLED, challenge);
            return;
        }

        context.setUser(user);
        // После входа по коду — предложить ключ доступа (PasskeyOfferAction решит, пора ли).
        context.getAuthenticationSession().setAuthNote(PasskeyOfferAction.NOTE_CODE_LOGIN, "true");
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
        String shown = CodeLoginNotes.masked(context.getAuthenticationSession()) ? target.masked() : target.display();
        LoginFormsProvider form = context.form().setExecution(context.getExecution().getId());
        codes.screen(context.getSession(), context.getRealm(), context.getConnection().getRemoteAddr(), target, state, shown)
            .forEach(form::setAttribute);
        return form;
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
