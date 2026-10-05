package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;

/**
 * Можно ли отправить код с учётом защиты от накрутки (без Keycloak): страны, лимиты по номеру, IP и диапазону,
 * капча, дневной бюджет. Пауза и блокировка после неверных вводов — в CodeRules; здесь — только растущая пауза.
 */
public final class CodeLimits {

    static final Duration HOUR = Duration.ofHours(1);
    static final Duration DAY = Duration.ofDays(1);

    private final CodeLimitPolicy policy;

    public CodeLimits(CodeLimitPolicy policy) {
        this.policy = policy;
    }

    public CodeLimitPolicy policy() {
        return policy;
    }

    /**
     * Что уже отправлено.
     *
     * @param target      на этот номер (почту)
     * @param ip          с этого IP
     * @param range       на диапазон номеров (для почты — пусто)
     * @param spentToday  расход проекта за сегодня, копейки
     */
    public record Usage(SendHistory target, SendHistory ip, SendHistory range, long spentToday) { }

    public sealed interface Decision {
        /** Отправлять. cost — сколько это стоит (копейки). */
        record Allow(long cost) implements Decision { }

        /** Номер не из разрешённых стран. */
        record CountryNotAllowed() implements Decision { }

        /** Сработал лимит: повтор через секунды. */
        record Limited(String limit, long retryAfterSeconds) implements Decision { }

        /** Нужна капча (её нет или она не прошла). */
        record CaptchaRequired() implements Decision { }

        /** Бюджет на сегодня кончился: SMS до полуночи не отправляются (почта и звонки — да). */
        record BudgetExhausted() implements Decision { }
    }

    /** Диапазон номера: первые rangeDigits цифр; для почты — null. */
    public String range(CodeTarget target) {
        if (target.channel() != CodeTarget.Channel.SMS) {
            return null;
        }
        String digits = target.value();
        return digits.length() <= policy.rangeDigits() ? digits : digits.substring(0, policy.rangeDigits());
    }

    /** Номер из разрешённой страны (почта — всегда да). */
    public boolean countryAllowed(CodeTarget target) {
        return target.channel() != CodeTarget.Channel.SMS
            || policy.countries().stream().anyMatch(country -> target.value().startsWith(country));
    }

    /** Пауза перед следующей отправкой на номер, если сейчас отправим: растёт с каждой отправкой за час. */
    public Duration cooldown(SendHistory target, Instant now) {
        return policy.cooldownAfter(target.count(now, HOUR) + 1);
    }

    /** Нужна ли капча перед отправкой — до её проверки (чтобы не ходить к сервису капчи зря). */
    public boolean captchaRequired(Usage usage, Instant now) {
        return usage.target().count(now, HOUR) >= policy.captchaAfter()
            || usage.ip().count(now, HOUR) >= policy.captchaAfter();
    }

    /**
     * Решение по отправке. captchaPassed — капча с этого запроса проверена и прошла.
     * Порядок: страна → лимиты (отказ даже с капчей) → капча → бюджет.
     */
    public Decision check(CodeTarget target, Usage usage, boolean captchaPassed, Instant now) {
        if (!countryAllowed(target)) {
            return new Decision.CountryNotAllowed();
        }

        Decision.Limited limited = limited(usage, now);
        if (limited != null) {
            return limited;
        }

        if (captchaRequired(usage, now) && !captchaPassed) {
            return new Decision.CaptchaRequired();
        }

        long cost = cost(target);
        if (cost > 0 && policy.dailyBudget() > 0 && usage.spentToday() + cost > policy.dailyBudget()) {
            return new Decision.BudgetExhausted();
        }
        return new Decision.Allow(cost);
    }

    /** Цена отправки, копейки: SMS — по тарифу, почта — бесплатно. */
    public long cost(CodeTarget target) {
        return target.channel() == CodeTarget.Channel.SMS ? policy.smsCost() : 0;
    }

    /** О какой доле бюджета пора оповестить при таком расходе: 100, 80 (порог из настроек) или 0 — ни о какой. */
    public int alertPercent(long spent) {
        if (policy.dailyBudget() <= 0) {
            return 0;
        }
        if (spent >= policy.dailyBudget()) {
            return 100;
        }
        return spent * 100 >= policy.dailyBudget() * policy.budgetAlertPercent() ? policy.budgetAlertPercent() : 0;
    }

    // Самый долгий из сработавших лимитов: отказ до тех пор, пока не освободятся все.
    private Decision.Limited limited(Usage usage, Instant now) {
        Decision.Limited worst = null;
        worst = worse(worst, "target-per-hour", usage.target().secondsUntilFree(now, HOUR, policy.targetPerHour()));
        worst = worse(worst, "target-per-day", usage.target().secondsUntilFree(now, DAY, policy.targetPerDay()));
        worst = worse(worst, "ip-per-hour", usage.ip().secondsUntilFree(now, HOUR, policy.ipPerHour()));
        worst = worse(worst, "range-per-hour", usage.range().secondsUntilFree(now, HOUR, policy.rangePerHour()));
        return worst;
    }

    private static Decision.Limited worse(Decision.Limited current, String limit, long seconds) {
        if (seconds <= 0 || (current != null && current.retryAfterSeconds() >= seconds)) {
            return current;
        }
        return new Decision.Limited(limit, seconds);
    }
}
