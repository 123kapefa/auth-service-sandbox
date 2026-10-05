package platform.keycloak.auth;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.util.Map;
import java.util.Optional;
import java.util.function.Function;

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

    static final String FORM = "platform-code-login.ftl";
    static final String FIELD_CODE = "code";
    static final String BUTTON_RESEND = "resend";

    private static final Logger LOG = Logger.getLogger(CodeLoginAuthenticator.class);
    private static final String CUSTOMER_MODE = "customer";

    private final CodeRules rules;
    private final Function<RealmModel, CodeLimits> limits;
    private final CaptchaVerifier captcha;
    private final String captchaSiteKey;
    private final CodeMetrics metrics;
    private final Map<CodeTarget.Channel, CodeSender> senders;
    private final Clock clock;

    CodeLoginAuthenticator(CodeRules rules, Function<RealmModel, CodeLimits> limits, CaptchaVerifier captcha,
                           String captchaSiteKey, CodeMetrics metrics, Map<CodeTarget.Channel, CodeSender> senders, Clock clock) {
        this.rules = rules;
        this.limits = limits;
        this.captcha = captcha;
        this.captchaSiteKey = captchaSiteKey;
        this.metrics = metrics;
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
                metrics.verified(context.getRealm().getName(), target.channel());
                signIn(context, target, success.state());
            }
            case CodeRules.VerifyResult.Wrong wrong -> {
                store.save(target, wrong.state(), clock.instant());
                fail(context, target, wrong.state(), "platformCodeWrong", wrong.attemptsLeft());
            }
            case CodeRules.VerifyResult.LockedNow locked -> {
                store.save(target, locked.state(), clock.instant());
                fail(context, target, locked.state(), "platformCodeLocked", locked.retryAfterSeconds());
            }
            case CodeRules.VerifyResult.Locked locked ->
                fail(context, target, current, "platformCodeLocked", locked.retryAfterSeconds());
            case CodeRules.VerifyResult.NoActiveCode ignored ->
                fail(context, target, current, "platformCodeExpired");
        }
    }

    private void send(AuthenticationFlowContext context, CodeTarget target) {
        RealmModel realm = context.getRealm();
        CodeStore store = new CodeStore(context.getSession(), realm);
        CodeState current = store.load(target);
        CodeLimits limits = this.limits.apply(realm);
        CodeLimitStore limitStore = new CodeLimitStore(context.getSession(), realm);
        Instant now = clock.instant();
        LocalDate today = LocalDate.ofInstant(now, limits.policy().zone());
        String ip = context.getConnection().getRemoteAddr();
        String range = limits.range(target);
        CodeLimits.Usage usage = limitStore.usage(target, ip, range, today);

        switch (rules.send(current, limits.cooldown(usage.target(), now))) {
            case CodeRules.SendResult.Sent sent -> {
                CodeLimits.Decision decision = decide(context, limits, target, usage, ip, now);
                if (!(decision instanceof CodeLimits.Decision.Allow allow)) {
                    refuse(context, target, current, limits, limitStore, today, decision);
                    return;
                }
                // Сохраняем только отправленный код: иначе пауза перед повтором началась бы без письма.
                if (!senders.get(target.channel()).send(context, target.value(), sent.code())) {
                    context.challenge(form(context, target, current).setError("platformCodeSendFailed").createForm(FORM));
                    return;
                }
                store.save(target, sent.state(), now);
                CodeLimitStore.Budget before = limitStore.recordSend(target, ip, range, allow.cost(), today, now);
                metrics.sent(realm.getName(), target.channel(), allow.cost());
                alertBudget(realm, limits, limitStore, today, before, limits.alertPercent(before.spent() + allow.cost()));
                context.challenge(form(context, target, sent.state()).createForm(FORM));
            }
            case CodeRules.SendResult.TooEarly early -> context.challenge(
                form(context, target, current).setInfo("platformCodeTooEarly", early.retryAfterSeconds()).createForm(FORM));
            case CodeRules.SendResult.Locked locked -> context.challenge(
                form(context, target, current).setError("platformCodeLocked", locked.retryAfterSeconds()).createForm(FORM));
        }
    }

    // Защита от накрутки; капчу проверяем у сервиса, только если она нужна и её прислали.
    private CodeLimits.Decision decide(AuthenticationFlowContext context, CodeLimits limits, CodeTarget target,
                                       CodeLimits.Usage usage, String ip, Instant now) {
        CodeLimits.Decision decision = limits.check(target, usage, false, now);
        if (!(decision instanceof CodeLimits.Decision.CaptchaRequired)) {
            return decision;
        }
        String token = context.getHttpRequest().getDecodedFormParameters().getFirst(CaptchaVerifier.FIELD);
        if (token == null || token.isBlank() || !captcha.verify(context.getSession(), token, ip)) {
            return decision;
        }
        return limits.check(target, usage, true, now);
    }

    private void refuse(AuthenticationFlowContext context, CodeTarget target, CodeState current, CodeLimits limits,
                        CodeLimitStore limitStore, LocalDate today, CodeLimits.Decision decision) {
        RealmModel realm = context.getRealm();
        LoginFormsProvider form = form(context, target, current);
        switch (decision) {
            case CodeLimits.Decision.CountryNotAllowed ignored -> {
                metrics.refused(realm.getName(), "country");
                form.setError("platformCodeCountryNotAllowed");
            }
            case CodeLimits.Decision.Limited limited -> {
                metrics.refused(realm.getName(), "limit");
                LOG.infof("Код на %s не отправлен: лимит %s, повтор через %d с", target.value(), limited.limit(),
                    limited.retryAfterSeconds());
                form.setError("platformCodeLimit", (limited.retryAfterSeconds() + 59) / 60);
            }
            case CodeLimits.Decision.CaptchaRequired ignored -> {
                metrics.refused(realm.getName(), "captcha");
                form.setAttribute("captchaRequired", "true").setError("platformCodeCaptchaRequired");
            }
            case CodeLimits.Decision.BudgetExhausted ignored -> {
                metrics.refused(realm.getName(), "budget");
                alertBudget(realm, limits, limitStore, today, limitStore.budget(today), 100);
                form.setError("platformCodeSmsUnavailable");
            }
            case CodeLimits.Decision.Allow ignored -> throw new IllegalStateException("Allow — не отказ");
        }
        context.challenge(form.createForm(FORM));
    }

    // Оповещение о расходе — один раз на каждый порог за сутки. Пока — в лог Keycloak (WARN, «[CODE BUDGET]»);
    // кому ещё слать (почта админам проекта), решает 10.7.
    private void alertBudget(RealmModel realm, CodeLimits limits, CodeLimitStore limitStore, LocalDate today,
                             CodeLimitStore.Budget budget, int percent) {
        if (percent == 0 || budget.alertedPercent() >= percent) {
            return;
        }
        limitStore.markAlerted(today, percent);
        metrics.budgetAlert(realm.getName(), percent);
        long budgetRub = limits.policy().dailyBudget() / 100;
        if (percent >= 100) {
            LOG.warnf("[CODE BUDGET] %s: дневной бюджет %d ₽ исчерпан — SMS выключены до полуночи (%s)",
                realm.getName(), budgetRub, limits.policy().zone());
        } else {
            LOG.warnf("[CODE BUDGET] %s: израсходовано %d %% дневного бюджета %d ₽", realm.getName(), percent, budgetRub);
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

        // Нужна ли капча для следующей отправки: тема заранее показывает её у кнопки «Отправить ещё раз».
        CodeLimits limits = this.limits.apply(context.getRealm());
        CodeLimitStore limitStore = new CodeLimitStore(context.getSession(), context.getRealm());
        CodeLimits.Usage usage = limitStore.usage(target, context.getConnection().getRemoteAddr(), limits.range(target),
            LocalDate.ofInstant(now, limits.policy().zone()));

        LoginFormsProvider form = context.form()
            .setExecution(context.getExecution().getId())
            .setAttribute("codeChannel", target.channel().name())
            .setAttribute("codeTarget", target.display())
            .setAttribute("codeResendAfter", Long.toString(resendAfter))
            .setAttribute("codeLength", Integer.toString(rules.codeLength()))
            .setAttribute("captchaRequired", Boolean.toString(limits.captchaRequired(usage, now)));
        if (captchaSiteKey != null) {
            form.setAttribute("captchaSiteKey", captchaSiteKey);
        }
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
