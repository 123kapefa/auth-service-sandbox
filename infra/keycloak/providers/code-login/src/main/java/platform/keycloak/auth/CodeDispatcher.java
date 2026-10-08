package platform.keycloak.auth;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.function.Function;

import org.jboss.logging.Logger;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;

/**
 * Отправка кода: пауза и блокировка (CodeRules), защита от накрутки (CodeLimits, капча, бюджет), канал (звонок, затем
 * SMS; почта — письмом) и экран кода (атрибуты страницы). Общая для входа по коду (CodeLoginAuthenticator) и смены
 * номера (UpdatePhoneAction): лимиты по номеру, IP и бюджет у них одни, а коды — разные (CodeStore с своим префиксом).
 */
final class CodeDispatcher {

    /** Код звонка — последние 4 цифры номера, с которого звонят. */
    static final int CALL_CODE_LENGTH = 4;

    private static final Logger LOG = Logger.getLogger(CodeDispatcher.class);

    private final CodeRules rules;
    private final Function<RealmModel, CodeLimits> limits;
    private final CaptchaVerifier captcha;
    private final String captchaSiteKey;
    private final CodeMetrics metrics;
    private final PhoneProvider phone;
    private final Clock clock;

    CodeDispatcher(CodeRules rules, Function<RealmModel, CodeLimits> limits, CaptchaVerifier captcha,
                   String captchaSiteKey, CodeMetrics metrics, PhoneProvider phone, Clock clock) {
        this.rules = rules;
        this.limits = limits;
        this.captcha = captcha;
        this.captchaSiteKey = captchaSiteKey;
        this.metrics = metrics;
        this.phone = phone;
        this.clock = clock;
    }

    /** Письмо с кодом — у входа по коду (EmailCodeSender); false — не ушло. */
    interface EmailDelivery {
        boolean send(String to, String code);

        EmailDelivery NONE = (to, code) -> false;
    }

    /** Где и для кого отправка: сессия, хранилище кодов, адрес клиента и ответ капчи (если прислали). */
    record Request(KeycloakSession session, RealmModel realm, CodeStore store, CodeTarget target, String ip,
                   String captchaToken) {
    }

    /** Чем кончилась отправка; current/state — состояние кода для экрана. */
    sealed interface Outcome {
        CodeState state();

        /** Код ушёл (или сделан вид, что ушёл, — способ выключен у человека). */
        record Sent(CodeState state) implements Outcome {
        }

        /** Рано: новый код — через retryAfter секунд. */
        record TooEarly(CodeState state, long retryAfter) implements Outcome {
        }

        /** Номер заблокирован после неверных вводов. */
        record Locked(CodeState state, long retryAfter) implements Outcome {
        }

        /** Отказ защиты от накрутки: сообщение для экрана; captcha — нужна капча. */
        record Refused(CodeState state, String message, Object[] parameters, boolean captcha) implements Outcome {
        }

        /** Канал не смог отправить. */
        record Failed(CodeState state) implements Outcome {
        }
    }

    /**
     * @param smsRequested   «Прислать SMS» вместо звонка
     * @param methodDisabled способ выключен у человека: код не уходит, экран — как после отправки
     */
    Outcome send(Request request, boolean smsRequested, boolean methodDisabled, EmailDelivery email) {
        RealmModel realm = request.realm();
        CodeTarget target = request.target();
        CodeState current = request.store().load(target);
        CodeLimits limits = this.limits.apply(realm);
        CodeLimitStore limitStore = new CodeLimitStore(request.session(), realm);
        Instant now = clock.instant();
        LocalDate today = LocalDate.ofInstant(now, limits.policy().zone());
        String range = limits.range(target);
        CodeLimits.Usage usage = limitStore.usage(target, request.ip(), range, today);

        return switch (rules.send(current, limits.cooldown(usage.target(), now))) {
            case CodeRules.SendResult.Sent sent -> {
                // «Ещё раз» после SMS — снова SMS; иначе на телефон — звонок.
                CodeDelivery delivery = choose(target, limits, smsRequested || current.delivery() == CodeDelivery.SMS);
                CodeLimits.Decision decision = decide(request, limits, delivery, usage, now);
                if (!(decision instanceof CodeLimits.Decision.Allow)) {
                    yield refuse(request, current, limits, limitStore, today, decision);
                }
                // Одна отправка на номер за раз: одновременные «Отправить ещё раз» не проходят паузу все вместе.
                if (!request.store().claimSend(target, current)) {
                    yield new Outcome.TooEarly(current, Math.max(1, limits.cooldown(usage.target(), now).toSeconds()));
                }
                // Лимиты — атомарно, до отправки: и одновременные запросы, и неудачные отправки (таймаут провайдера
                // после звонка) их расходуют.
                CodeLimits.Decision.Limited limited = limitStore.reserve(target, request.ip(), range, limits.policy(), now);
                if (limited != null) {
                    request.store().releaseSend(target, current);
                    yield refuse(request, current, limits, limitStore, today, limited);
                }
                if (methodDisabled) {
                    yield pretendSent(request, sent, delivery, limitStore, range, today, now);
                }
                // Расход — тоже атомарно: одновременные SMS не уйдут за бюджет. Неудачная отправка расход не возвращает:
                // «не удалось» бывает и после совершённого звонка или SMS (таймаут ответа провайдера), а платим за них.
                List<Map.Entry<String, Integer>> spend = new ArrayList<>();
                if (!limitStore.reserveSpend(delivery, limits.policy(), today, spend)) {
                    request.store().releaseSend(target, current);
                    yield refuse(request, current, limits, limitStore, today, new CodeLimits.Decision.BudgetExhausted());
                }
                Optional<String> code = deliver(request, delivery, sent.code(), email);
                // Звонок не удался (провайдер недоступен) — сразу запасная SMS, если она у проекта есть и в бюджете.
                // Капча и лимиты уже пройдены этим запросом.
                if (code.isEmpty() && delivery == CodeDelivery.CALL && limits.policy().allows(CodeDelivery.SMS)) {
                    delivery = CodeDelivery.SMS;
                    if (!limitStore.reserveSpend(delivery, limits.policy(), today, spend)) {
                        failed(request, limitStore, range, today, now);
                        yield refuse(request, current, limits, limitStore, today, new CodeLimits.Decision.BudgetExhausted());
                    }
                    code = deliver(request, delivery, sent.code(), email);
                }
                // Сохраняем только отправленный код: иначе пауза перед повтором началась бы без письма.
                if (code.isEmpty()) {
                    failed(request, limitStore, range, today, now);
                    yield new Outcome.Failed(current);
                }
                CodeState state = sent.state().sentAs(code.get(), delivery);
                long cost = limits.cost(delivery);
                request.store().save(target, state, now);
                long spent = limitStore.spent(today, limits.policy());
                int alert = limitStore.recordSend(target, request.ip(), range, delivery, cost, spent, today, now,
                    limits::alertPercent);
                metrics.sent(realm.getName(), delivery, cost);
                if (alert > 0) {
                    alertBudget(request.session(), realm, limits, alert, Math.max(spent, usage.spentToday() + cost));
                }
                yield new Outcome.Sent(state);
            }
            case CodeRules.SendResult.TooEarly early -> new Outcome.TooEarly(current, early.retryAfterSeconds());
            case CodeRules.SendResult.Locked locked -> new Outcome.Locked(current, locked.retryAfterSeconds());
        };
    }

    /** Проверка введённого кода (сохраняет новое состояние, кроме «нет кода» и «заблокирован»). */
    CodeRules.VerifyResult verify(CodeStore store, CodeTarget target, String input) {
        CodeState current = store.load(target);
        Instant now = clock.instant();
        CodeRules.VerifyResult result = rules.verify(current, input,
            () -> store.claimAttempt(target, current, rules.maxAttempts(), now));
        switch (result) {
            case CodeRules.VerifyResult.Success success -> store.save(target, success.state(), clock.instant());
            case CodeRules.VerifyResult.Wrong wrong -> store.save(target, wrong.state(), clock.instant());
            case CodeRules.VerifyResult.LockedNow locked -> store.save(target, locked.state(), clock.instant());
            default -> {
                // состояние не меняется
            }
        }
        return result;
    }

    /** Атрибуты экрана кода: канал, куда, пауза, длина кода, «Прислать SMS», капча. */
    Map<String, String> screen(KeycloakSession session, RealmModel realm, String ip, CodeTarget target, CodeState state,
                               String shownTarget) {
        Instant now = clock.instant();
        long resendAfter = state.resendAllowedAt() == null || !now.isBefore(state.resendAllowedAt())
            ? 0
            : (Duration.between(now, state.resendAllowedAt()).toMillis() + 999) / 1000;

        // Нужна ли капча для следующей отправки: тема заранее показывает её у кнопки «Отправить ещё раз».
        CodeLimits limits = this.limits.apply(realm);
        // Кода ещё не было (капча до первой отправки) — как он уйдёт: на телефон обычно звонком.
        CodeDelivery delivery = state.delivery() != null ? state.delivery() : choose(target, limits, false);
        CodeLimitStore limitStore = new CodeLimitStore(session, realm);
        CodeLimits.Usage usage = limitStore.usage(target, ip, limits.range(target),
            LocalDate.ofInstant(now, limits.policy().zone()));

        Map<String, String> attributes = new LinkedHashMap<>();
        attributes.put("codeChannel", target.channel().name());
        attributes.put("codeDelivery", delivery.name());
        // «Прислать SMS» — на экране звонка, если SMS у проекта есть.
        attributes.put("codeSmsAvailable", Boolean.toString(
            delivery == CodeDelivery.CALL && limits.policy().allows(CodeDelivery.SMS)));
        attributes.put("codeTarget", shownTarget);
        attributes.put("codeResendAfter", Long.toString(resendAfter));
        attributes.put("codeLength", Integer.toString(delivery == CodeDelivery.CALL ? CALL_CODE_LENGTH : rules.codeLength()));
        attributes.put("captchaRequired", Boolean.toString(limits.captchaRequired(usage, now)));
        if (captchaSiteKey != null) {
            attributes.put("captchaSiteKey", captchaSiteKey);
        }
        return attributes;
    }

    /** Как ушёл действующий код; кода ещё не было — как ушёл бы по умолчанию для канала. */
    static CodeDelivery delivery(CodeTarget target, CodeState state) {
        if (state.delivery() != null) {
            return state.delivery();
        }
        return target.channel() == CodeTarget.Channel.EMAIL ? CodeDelivery.EMAIL : CodeDelivery.SMS;
    }

    // У человека выключен этот способ (SignInMethods): код не уходит, а экран — как после отправки (по ответу не
    // понять, есть ли аккаунт и что выключено). Сохраняется код, которого никто не знает: пауза и лимиты — как обычно,
    // но без расхода бюджета.
    private Outcome pretendSent(Request request, CodeRules.SendResult.Sent sent, CodeDelivery delivery,
                                CodeLimitStore limitStore, String range, LocalDate today, Instant now) {
        CodeState state = sent.state().sentAs(sent.code(), delivery);
        request.store().save(request.target(), state, now);
        limitStore.recordSend(request.target(), request.ip(), range, null, 0, 0, today, now, spent -> 0);
        metrics.refused(request.realm().getName(), "method-disabled");
        LOG.infof("Код на %s не отправлен: способ входа выключен у пользователя", request.target().value());
        return new Outcome.Sent(state);
    }

    // Отправка не удалась: она всё равно считается в истории (растущая пауза, капча — как после отправки), а занятость
    // отправки (claimSend) не снимается до своего срока — повтор не раньше, чем через полминуты. Иначе медленный или
    // недоступный провайдер давал бы звонить жертве без пауз (лимиты в час — тоже, но они крупнее).
    private void failed(Request request, CodeLimitStore limitStore, String range, LocalDate today, Instant now) {
        limitStore.recordSend(request.target(), request.ip(), range, null, 0, 0, today, now, spent -> 0);
        metrics.refused(request.realm().getName(), "failed");
    }

    // Почта — письмом. Телефон — звонком-сбросом (дешевле); SMS — если попросили или звонки у проекта выключены.
    private static CodeDelivery choose(CodeTarget target, CodeLimits limits, boolean smsWanted) {
        if (target.channel() == CodeTarget.Channel.EMAIL) {
            return CodeDelivery.EMAIL;
        }
        if (smsWanted && limits.policy().allows(CodeDelivery.SMS)) {
            return CodeDelivery.SMS;
        }
        return limits.policy().allows(CodeDelivery.CALL) ? CodeDelivery.CALL : CodeDelivery.SMS;
    }

    // Код, который ушёл (у звонка — последние 4 цифры номера, их выбирает провайдер), или пусто — не удалось.
    private Optional<String> deliver(Request request, CodeDelivery delivery, String generated, EmailDelivery email) {
        String to = request.target().value();
        return switch (delivery) {
            case CALL -> phone.call(request.session(), to);
            case SMS -> phone.sms(request.session(), to, generated) ? Optional.of(generated) : Optional.empty();
            case EMAIL -> email.send(to, generated) ? Optional.of(generated) : Optional.empty();
        };
    }

    // Защита от накрутки; капчу проверяем у сервиса, только если она нужна и её прислали.
    private CodeLimits.Decision decide(Request request, CodeLimits limits, CodeDelivery delivery, CodeLimits.Usage usage,
                                       Instant now) {
        CodeLimits.Decision decision = limits.check(request.target(), delivery, usage, false, now);
        if (!(decision instanceof CodeLimits.Decision.CaptchaRequired)) {
            return decision;
        }
        String token = request.captchaToken();
        if (token == null || token.isBlank() || !captcha.verify(request.session(), token, request.ip())) {
            return decision;
        }
        return limits.check(request.target(), delivery, usage, true, now);
    }

    private Outcome refuse(Request request, CodeState current, CodeLimits limits, CodeLimitStore limitStore,
                           LocalDate today, CodeLimits.Decision decision) {
        RealmModel realm = request.realm();
        return switch (decision) {
            case CodeLimits.Decision.CountryNotAllowed ignored -> {
                metrics.refused(realm.getName(), "country");
                yield new Outcome.Refused(current, "platformCodeCountryNotAllowed", new Object[0], false);
            }
            case CodeLimits.Decision.Limited limited -> {
                metrics.refused(realm.getName(), "limit");
                LOG.infof("Код на %s не отправлен: лимит %s, повтор через %d с", request.target().value(), limited.limit(),
                    limited.retryAfterSeconds());
                yield new Outcome.Refused(current, "platformCodeLimit",
                    new Object[] {(limited.retryAfterSeconds() + 59) / 60}, false);
            }
            case CodeLimits.Decision.CaptchaRequired ignored -> {
                metrics.refused(realm.getName(), "captcha");
                yield new Outcome.Refused(current, "platformCodeCaptchaRequired", new Object[0], true);
            }
            case CodeLimits.Decision.BudgetExhausted ignored -> {
                metrics.refused(realm.getName(), "budget");
                if (limitStore.markAlerted(today, 100)) {
                    alertBudget(request.session(), realm, limits, 100, limitStore.budget(today).spent());
                }
                yield new Outcome.Refused(current, "platformCodeSmsUnavailable", new Object[0], false);
            }
            case CodeLimits.Decision.Allow ignored -> throw new IllegalStateException("Allow — не отказ");
        };
    }

    // Оповещение о расходе — один раз на каждый порог за сутки (отметка в CodeLimitStore): строка в логе Keycloak
    // (WARN, «[CODE BUDGET]»), метрика и письмо админам проекта (BudgetAlerts).
    private void alertBudget(KeycloakSession session, RealmModel realm, CodeLimits limits, int percent, long spent) {
        metrics.budgetAlert(realm.getName(), percent);
        CodeLimitPolicy policy = limits.policy();
        long budgetRub = policy.dailyBudget() / 100;
        if (percent >= 100) {
            LOG.warnf("[CODE BUDGET] %s: дневной бюджет %d ₽ исчерпан — SMS выключены до полуночи (%s)",
                realm.getName(), budgetRub, policy.zone());
        } else {
            LOG.warnf("[CODE BUDGET] %s: израсходовано %d %% дневного бюджета %d ₽", realm.getName(), percent, budgetRub);
        }
        BudgetAlerts.send(session, realm, percent, spent, policy.dailyBudget(), policy.zone());
    }

    /** Код введён верно: метрика и счётчик суток проекта (доля введённых). */
    void verified(KeycloakSession session, RealmModel realm, CodeDelivery delivery) {
        metrics.verified(realm.getName(), delivery);
        new CodeLimitStore(session, realm).recordVerified(LocalDate.ofInstant(clock.instant(), limits.apply(realm).policy().zone()));
    }
}
