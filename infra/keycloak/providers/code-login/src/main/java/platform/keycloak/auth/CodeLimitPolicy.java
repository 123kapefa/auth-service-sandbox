package platform.keycloak.auth;

import java.time.Duration;
import java.time.ZoneId;
import java.util.Arrays;
import java.util.List;
import java.util.function.Function;

/**
 * Защита от накрутки кодов: лимиты отправок, растущая пауза, страны, капча, дневной бюджет.
 *
 * @param cooldowns          пауза перед повторной отправкой на тот же номер: 1-я отправка за час → [0], 2-я → [1]…;
 *                           дальше — последняя
 * @param targetPerHour      отправок на номер (почту) за час
 * @param targetPerDay       отправок на номер (почту) за сутки
 * @param ipPerHour          отправок с одного IP за час
 * @param rangePerHour       отправок на диапазон номеров за час
 * @param rangeDigits        диапазон — первые столько цифр номера
 * @param captchaAfter       после стольких отправок за час (с IP или на номер) нужна капча
 * @param countries          коды стран, на номера которых шлём (по началу номера: «7» — Россия)
 * @param dailyBudget        дневной бюджет проекта, копейки; 0 — без бюджета
 * @param smsCost            цена одной SMS, копейки
 * @param callCost           цена одного звонка-сброса, копейки
 * @param phoneChannels      как слать код на телефон: звонок и/или SMS (порядок не важен — звонок всегда первый)
 * @param budgetAlertPercent при такой доле бюджета — оповещение
 * @param zone               в каком часовом поясе начинаются сутки бюджета
 */
public record CodeLimitPolicy(
    List<Duration> cooldowns,
    int targetPerHour,
    int targetPerDay,
    int ipPerHour,
    int rangePerHour,
    int rangeDigits,
    int captchaAfter,
    List<String> countries,
    long dailyBudget,
    long smsCost,
    long callCost,
    List<CodeDelivery> phoneChannels,
    int budgetAlertPercent,
    ZoneId zone) {

    /** Значения по умолчанию (план этапа 10). Настройки проекта подключаются в 10.7. */
    public static final CodeLimitPolicy DEFAULTS = new CodeLimitPolicy(
        List.of(Duration.ofSeconds(60), Duration.ofMinutes(2), Duration.ofMinutes(5), Duration.ofMinutes(15)),
        5,          // на номер за час
        10,         // на номер за сутки
        10,         // с IP за час
        20,         // на диапазон за час
        7,          // диапазон — первые 7 цифр
        2,          // капча после 2 отправок за час
        List.of("7"),
        100_000,    // 1000 ₽ в сутки
        400,        // SMS — 4 ₽ (уточнить у провайдера)
        100,        // звонок — 1 ₽ (уточнить у провайдера)
        List.of(CodeDelivery.CALL, CodeDelivery.SMS),
        80,
        ZoneId.of("Europe/Moscow"));

    /** Атрибуты realm с настройками проекта — их пишет API админки (10.7), имена — как у ключей ниже. */
    public static final String REALM_ATTRIBUTE_PREFIX = "platform.codes.";

    public CodeLimitPolicy {
        if (cooldowns.isEmpty()) {
            throw new IllegalArgumentException("cooldowns: нужна хотя бы одна пауза");
        }
        cooldowns = List.copyOf(cooldowns);
        countries = List.copyOf(countries);
        phoneChannels = List.copyOf(phoneChannels);
        if (phoneChannels.isEmpty() || phoneChannels.contains(CodeDelivery.EMAIL)) {
            throw new IllegalArgumentException("phone-channels: call и/или sms");
        }
    }

    /** Можно ли слать код на телефон этим способом (звонок, SMS). */
    public boolean allows(CodeDelivery delivery) {
        return delivery == CodeDelivery.EMAIL || phoneChannels.contains(delivery);
    }

    /** Пауза после отправки, которая за час будет sendsInHour-й по счёту (с 1). */
    public Duration cooldownAfter(int sendsInHour) {
        int index = Math.min(Math.max(sendsInHour, 1), cooldowns.size()) - 1;
        return cooldowns.get(index);
    }

    /**
     * Политика из настроек: setting(ключ) — значение или null (тогда берётся из base). Ключи — в kebab-case:
     * cooldowns («60,120,300,900», секунды), target-per-hour, target-per-day, ip-per-hour, range-per-hour, range-digits,
     * captcha-after, countries («7,375»), daily-budget, sms-cost, call-cost (копейки), phone-channels («call,sms», «sms»),
     * budget-alert-percent, zone.
     * Неверное значение — ошибка: лучше не отправить код, чем отправить без лимита.
     */
    public static CodeLimitPolicy from(Function<String, String> setting, CodeLimitPolicy base) {
        return new CodeLimitPolicy(
            list(setting.apply("cooldowns"), base.cooldowns(), value -> Duration.ofSeconds(Long.parseLong(value))),
            integer(setting.apply("target-per-hour"), base.targetPerHour()),
            integer(setting.apply("target-per-day"), base.targetPerDay()),
            integer(setting.apply("ip-per-hour"), base.ipPerHour()),
            integer(setting.apply("range-per-hour"), base.rangePerHour()),
            integer(setting.apply("range-digits"), base.rangeDigits()),
            integer(setting.apply("captcha-after"), base.captchaAfter()),
            list(setting.apply("countries"), base.countries(), Function.identity()),
            number(setting.apply("daily-budget"), base.dailyBudget()),
            number(setting.apply("sms-cost"), base.smsCost()),
            number(setting.apply("call-cost"), base.callCost()),
            list(setting.apply("phone-channels"), base.phoneChannels(),
                value -> CodeDelivery.valueOf(value.toUpperCase(java.util.Locale.ROOT))),
            integer(setting.apply("budget-alert-percent"), base.budgetAlertPercent()),
            blank(setting.apply("zone")) ? base.zone() : ZoneId.of(setting.apply("zone").trim()));
    }

    private static int integer(String value, int fallback) {
        return blank(value) ? fallback : Integer.parseInt(value.trim());
    }

    private static long number(String value, long fallback) {
        return blank(value) ? fallback : Long.parseLong(value.trim());
    }

    private static <T> List<T> list(String value, List<T> fallback, Function<String, T> parse) {
        if (blank(value)) {
            return fallback;
        }
        return Arrays.stream(value.split(",")).map(String::trim).filter(item -> !item.isEmpty()).map(parse).toList();
    }

    private static boolean blank(String value) {
        return value == null || value.isBlank();
    }
}
