package platform.keycloak.auth;

import java.io.IOException;
import java.text.MessageFormat;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Properties;

import jakarta.ws.rs.core.MultivaluedMap;

import org.jboss.logging.Logger;
import org.keycloak.authentication.InitiatedActionSupport;
import org.keycloak.authentication.RequiredActionContext;
import org.keycloak.authentication.RequiredActionProvider;
import org.keycloak.email.EmailException;
import org.keycloak.forms.login.LoginFormsProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;

/**
 * Смена номера самим человеком (10.5) — действие Keycloak, запускается из кабинета (kc_action=platform-update-phone),
 * как смена почты. Вошёл больше 5 минут назад — Keycloak сначала попросит войти заново.
 * <ol>
 *   <li>Новый номер: не чаще раза в сутки, настоящий, другой, не занят другим аккаунтом (PhoneChangeRules).</li>
 *   <li>Код на новый номер — как при входе: звонок, затем SMS, те же лимиты и бюджет (CodeDispatcher), но свои коды.</li>
 *   <li>Верный код — номер изменён: логин-номер и атрибут phone; прежний номер больше не ведёт в этот аккаунт (код на
 *       него создаст новый). Письмо на почту и SMS на старый номер; в журнале событий Keycloak — прежний и новый.</li>
 * </ol>
 * Страница — platform-update-phone.ftl (атрибуты phoneStep: phone | code, и экрана кода — как у входа по коду).
 */
public final class UpdatePhoneAction implements RequiredActionProvider {

    static final String FORM = "platform-update-phone.ftl";
    static final String FIELD_PHONE = "phone";
    static final String BUTTON_CHANGE_PHONE = "changePhone";

    /** Новый номер (цифрами), на который ушёл код. */
    static final String NOTE_TARGET = "platform.phone-change.target";

    private static final Logger LOG = Logger.getLogger(UpdatePhoneAction.class);
    private static final int MAX_AUTH_AGE = (int) Duration.ofMinutes(5).toSeconds();

    private final CodeDispatcher codes;
    private final Clock clock;

    UpdatePhoneAction(CodeDispatcher codes, Clock clock) {
        this.codes = codes;
        this.clock = clock;
    }

    @Override
    public InitiatedActionSupport initiatedActionSupport() {
        return InitiatedActionSupport.SUPPORTED;
    }

    @Override
    public int getMaxAuthAge(KeycloakSession session) {
        return MAX_AUTH_AGE;
    }

    @Override
    public void evaluateTriggers(RequiredActionContext context) {
        // только по запросу из кабинета
    }

    @Override
    public void requiredActionChallenge(RequiredActionContext context) {
        context.getAuthenticationSession().removeAuthNote(NOTE_TARGET);
        LoginFormsProvider form = phoneForm(context);
        PhoneChangeRules.nextAllowed(context.getUser().getFirstAttribute(PhoneChangeRules.CHANGED_AT), clock.instant())
            .ifPresent(next -> tooOften(form, next));
        context.challenge(form.createForm(FORM));
    }

    @Override
    public void processAction(RequiredActionContext context) {
        MultivaluedMap<String, String> formData = context.getHttpRequest().getDecodedFormParameters();
        String target = context.getAuthenticationSession().getAuthNote(NOTE_TARGET);
        if (formData.containsKey(BUTTON_CHANGE_PHONE)) {
            requiredActionChallenge(context);
            return;
        }
        if (target == null) {
            choosePhone(context, formData.getFirst(FIELD_PHONE));
            return;
        }
        CodeTarget phone = new CodeTarget(CodeTarget.Channel.SMS, target);
        if (formData.containsKey(CodeLoginAuthenticator.BUTTON_RESEND) || formData.containsKey(CodeLoginAuthenticator.BUTTON_SEND_SMS)) {
            send(context, phone, formData.containsKey(CodeLoginAuthenticator.BUTTON_SEND_SMS));
            return;
        }
        CodeStore store = store(context);
        CodeState current = store.load(phone);
        switch (codes.verify(store, phone, formData.getFirst(CodeLoginAuthenticator.FIELD_CODE))) {
            case CodeRules.VerifyResult.Success ignored -> {
                codes.verified(context.getSession(), context.getRealm(), CodeDispatcher.delivery(phone, current));
                change(context, phone);
            }
            case CodeRules.VerifyResult.Wrong wrong -> codeError(context, phone, wrong.state(), "platformCodeWrong", wrong.attemptsLeft());
            case CodeRules.VerifyResult.LockedNow locked ->
                codeError(context, phone, locked.state(), "platformCodeLocked", locked.retryAfterSeconds());
            case CodeRules.VerifyResult.Locked locked ->
                codeError(context, phone, current, "platformCodeLocked", locked.retryAfterSeconds());
            case CodeRules.VerifyResult.NoActiveCode ignored -> codeError(context, phone, current, "platformCodeExpired");
        }
    }

    // Шаг 1: новый номер → проверки → код на него.
    private void choosePhone(RequiredActionContext context, String input) {
        UserModel user = context.getUser();
        PhoneChangeRules.Check check = check(context, user, input == null ? "" : input);
        if (!(check instanceof PhoneChangeRules.Check.Ok ok)) {
            LoginFormsProvider form = phoneForm(context);
            switch (check) {
                case PhoneChangeRules.Check.TooOften often -> tooOften(form, often.nextAllowed());
                case PhoneChangeRules.Check.Invalid ignored -> form.setError("platformPhoneInvalid");
                case PhoneChangeRules.Check.Same ignored -> form.setError("platformPhoneSame");
                case PhoneChangeRules.Check.Taken ignored -> form.setError("platformPhoneTaken");
                case PhoneChangeRules.Check.Ok ignored -> throw new IllegalStateException("Ok — не ошибка");
            }
            context.challenge(form.createForm(FORM));
            return;
        }
        context.getAuthenticationSession().setAuthNote(NOTE_TARGET, ok.login());
        send(context, new CodeTarget(CodeTarget.Channel.SMS, ok.login()), false);
    }

    private PhoneChangeRules.Check check(RequiredActionContext context, UserModel user, String input) {
        KeycloakSession session = context.getSession();
        RealmModel realm = context.getRealm();
        return PhoneChangeRules.check(input, AccountNotices.phone(user),
            user.getFirstAttribute(PhoneChangeRules.CHANGED_AT), clock.instant(),
            login -> taken(session, realm, user, login));
    }

    // Занят: логин другого аккаунта или его атрибут phone.
    private static boolean taken(KeycloakSession session, RealmModel realm, UserModel self, String login) {
        UserModel byLogin = session.users().getUserByUsername(realm, login);
        if (byLogin != null && !byLogin.getId().equals(self.getId())) {
            return true;
        }
        return session.users().searchForUserByUserAttributeStream(realm, "phone", PhoneNormalizer.toE164(login))
            .anyMatch(other -> !other.getId().equals(self.getId()));
    }

    private void send(RequiredActionContext context, CodeTarget phone, boolean smsRequested) {
        CodeDispatcher.Request request = new CodeDispatcher.Request(context.getSession(), context.getRealm(), store(context),
            phone, context.getConnection().getRemoteAddr(),
            context.getHttpRequest().getDecodedFormParameters().getFirst(CaptchaVerifier.FIELD));
        CodeDispatcher.Outcome outcome = codes.send(request, smsRequested, false, CodeDispatcher.EmailDelivery.NONE);
        LoginFormsProvider form = codeForm(context, phone, outcome.state());
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

    // Код верный — меняем. Правила проверяются ещё раз: пока вводили код, номер мог занять другой.
    private void change(RequiredActionContext context, CodeTarget phone) {
        UserModel user = context.getUser();
        PhoneChangeRules.Check check = check(context, user, phone.value());
        if (!(check instanceof PhoneChangeRules.Check.Ok)) {
            context.getAuthenticationSession().removeAuthNote(NOTE_TARGET);
            LoginFormsProvider form = phoneForm(context);
            form.setError(check instanceof PhoneChangeRules.Check.Taken ? "platformPhoneTaken" : "platformPhoneInvalid");
            context.challenge(form.createForm(FORM));
            return;
        }

        String previous = AccountNotices.phone(user);
        // Логин-номер меняется вместе с номером: код на старый номер больше не найдёт этот аккаунт.
        if (previous != null && previous.equals(user.getUsername())) {
            user.setUsername(phone.value());
        }
        user.setSingleAttribute("phone", phone.display());
        user.setSingleAttribute(PhoneChangeRules.CHANGED_AT, clock.instant().toString());
        if (previous != null) {
            user.setSingleAttribute(PhoneChangeRules.PREVIOUS, PhoneNormalizer.toE164(previous));
        } else {
            user.removeAttribute(PhoneChangeRules.PREVIOUS);
        }
        context.getAuthenticationSession().removeAuthNote(NOTE_TARGET);
        context.getEvent()
            .detail("previous_phone", previous == null ? "" : PhoneNormalizer.toE164(previous))
            .detail("updated_phone", phone.display());
        LOG.infof("Номер изменён: %s → %s", previous, phone.value());
        notifyChange(context.getSession(), context.getRealm(), user, previous, phone);
        context.success();
    }

    // Письмо на почту и SMS на старый номер. «По возможности»: не ушло — номер всё равно изменён.
    private static void notifyChange(KeycloakSession session, RealmModel realm, UserModel user, String previous,
                                     CodeTarget phone) {
        try {
            Properties messages = AccountNotices.messages(session, realm, user);
            String realmName = AccountNotices.realmName(realm);
            String notYou = messages.getProperty("platformSignInMethodsNotYou", "");
            String text = MessageFormat.format(messages.getProperty("platformPhoneChangedText", ""), realmName, phone.masked());
            AccountNotices.email(session, realm, user, "platformPhoneChangedEmailSubject", "platform-phone-changed.ftl", text, notYou);
            if (previous != null) {
                String sms = MessageFormat.format(messages.getProperty("platformPhoneChangedOldSms", ""), realmName, phone.masked());
                AccountNotices.phoneProvider(session).message(session, previous, (sms + " " + notYou).trim());
            }
        } catch (EmailException | IOException e) {
            LOG.warnf(e, "Уведомление о смене номера для %s не отправлено", user.getUsername());
        }
    }

    private void codeError(RequiredActionContext context, CodeTarget phone, CodeState state, String message, Object... parameters) {
        context.challenge(codeForm(context, phone, state).setError(message, parameters).createForm(FORM));
    }

    private static LoginFormsProvider phoneForm(RequiredActionContext context) {
        LoginFormsProvider form = context.form().setAttribute("phoneStep", "phone");
        String current = AccountNotices.phone(context.getUser());
        if (current != null) {
            form.setAttribute("phoneCurrent", PhoneNormalizer.toE164(current));
        }
        return form;
    }

    private void tooOften(LoginFormsProvider form, Instant next) {
        form.setAttribute("phoneNextAllowed", next.toString());
        long hours = Math.max(1, (Duration.between(clock.instant(), next).toMinutes() + 59) / 60);
        form.setError("platformPhoneTooOften", hours);
    }

    private LoginFormsProvider codeForm(RequiredActionContext context, CodeTarget phone, CodeState state) {
        LoginFormsProvider form = context.form().setAttribute("phoneStep", "code");
        codes.screen(context.getSession(), context.getRealm(), context.getConnection().getRemoteAddr(), phone, state,
            phone.display()).forEach(form::setAttribute);
        return form;
    }

    private static CodeStore store(RequiredActionContext context) {
        return new CodeStore(context.getSession(), context.getRealm(), CodeStore.PHONE_CHANGE);
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
