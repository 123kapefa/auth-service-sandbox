package platform.keycloak.auth;

import java.time.LocalDate;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Pattern;

/**
 * Коды проекта в админке («Вход и коды»): что админ меняет сам — способы отправки на телефон, страны, дневной бюджет и
 * лимиты защиты от накрутки — и расход за сегодня. Меняемое хранится в атрибутах realm (platform.codes.*), поверх
 * настроек сервера; цены, пауза, диапазон, порог оповещения и часовой пояс — только настройками сервера.
 * Здесь — только значения и их проверка, без Keycloak (ресурс — CodeSettingsResource).
 */
public final class CodeSettings {

    /** Роль realm служебной учётки сервиса входа: менять настройки входа и кодов проекта (не весь realm). */
    public static final String MANAGE_ROLE = "manage-sign-in";

    public static final long BUDGET_MAX = 1_000_000_000L; // 10 млн ₽ в сутки, копейки
    public static final int COUNTRIES_MAX = 20;

    private static final Pattern COUNTRY = Pattern.compile("[1-9][0-9]{0,3}");

    private CodeSettings() {
    }

    /** Что меняет админ. Присылают всё целиком (как отдал GET); бюджет — копейки, 0 — без бюджета. */
    public static final class Representation {
        public List<String> phoneChannels;
        public List<String> countries;
        public Long dailyBudget;
        public Integer targetPerHour;
        public Integer targetPerDay;
        public Integer ipPerHour;
        public Integer rangePerHour;
        public Integer captchaAfter;
    }

    /** Настройки, по которым коды отправляются сейчас: меняемые и (только для показа) настройки сервера. */
    public static Map<String, Object> view(CodeLimitPolicy policy) {
        Map<String, Object> view = new LinkedHashMap<>();
        view.put("phoneChannels", policy.phoneChannels().stream()
            .sorted().map(delivery -> delivery.name().toLowerCase(Locale.ROOT)).toList());
        view.put("countries", policy.countries());
        view.put("dailyBudget", policy.dailyBudget());
        view.put("targetPerHour", policy.targetPerHour());
        view.put("targetPerDay", policy.targetPerDay());
        view.put("ipPerHour", policy.ipPerHour());
        view.put("rangePerHour", policy.rangePerHour());
        view.put("captchaAfter", policy.captchaAfter());
        view.put("rangeDigits", policy.rangeDigits());
        view.put("smsCost", policy.smsCost());
        view.put("callCost", policy.callCost());
        view.put("budgetAlertPercent", policy.budgetAlertPercent());
        view.put("zone", policy.zone().getId());
        return view;
    }

    /** Ошибка проверки по-русски или null — всё верно. */
    public static String validate(Representation settings) {
        if (settings == null) {
            return "Нет настроек.";
        }
        if (settings.phoneChannels == null || settings.phoneChannels.isEmpty()) {
            return "Включите звонки или SMS: без них код на телефон не отправить.";
        }
        if (!settings.phoneChannels.stream().allMatch(channel -> "call".equals(channel) || "sms".equals(channel))
            || new HashSet<>(settings.phoneChannels).size() != settings.phoneChannels.size()) {
            return "Способы отправки на телефон — call и sms.";
        }
        if (settings.countries == null || settings.countries.isEmpty()) {
            return "Укажите хотя бы одну страну.";
        }
        if (settings.countries.size() > COUNTRIES_MAX) {
            return "Стран — не больше " + COUNTRIES_MAX + ".";
        }
        for (String country : settings.countries) {
            if (country == null || !COUNTRY.matcher(country).matches()) {
                return "Страна — код из цифр, без «+»: 7, 375.";
            }
        }
        if (new HashSet<>(settings.countries).size() != settings.countries.size()) {
            return "Страны повторяются.";
        }
        if (settings.dailyBudget == null || settings.dailyBudget < 0 || settings.dailyBudget > BUDGET_MAX) {
            return "Дневной бюджет — от 0 до " + BUDGET_MAX / 100 + " ₽ (0 — без бюджета).";
        }
        String error = range("На номер в час", settings.targetPerHour, 1, 100);
        if (error == null) {
            error = range("На номер в сутки", settings.targetPerDay, 1, 1000);
        }
        if (error == null && settings.targetPerDay < settings.targetPerHour) {
            error = "На номер в сутки — не меньше, чем в час.";
        }
        if (error == null) {
            error = range("С одного IP в час", settings.ipPerHour, 1, 10_000);
        }
        if (error == null) {
            error = range("На диапазон номеров в час", settings.rangePerHour, 1, 100_000);
        }
        if (error == null) {
            error = range("Капча после", settings.captchaAfter, 0, 100);
        }
        return error;
    }

    /** Атрибуты realm (без префикса) для проверенных настроек — имена ключей как в CodeLimitPolicy.from. */
    public static Map<String, String> attributes(Representation settings) {
        Map<String, String> attributes = new LinkedHashMap<>();
        attributes.put("phone-channels", String.join(",", settings.phoneChannels.stream().sorted().toList()));
        attributes.put("countries", String.join(",", settings.countries));
        attributes.put("daily-budget", Long.toString(settings.dailyBudget));
        attributes.put("target-per-hour", Integer.toString(settings.targetPerHour));
        attributes.put("target-per-day", Integer.toString(settings.targetPerDay));
        attributes.put("ip-per-hour", Integer.toString(settings.ipPerHour));
        attributes.put("range-per-hour", Integer.toString(settings.rangePerHour));
        attributes.put("captcha-after", Integer.toString(settings.captchaAfter));
        return attributes;
    }

    /**
     * Расход за сутки (по часовому поясу бюджета): сколько кодов ушло каждым способом и во что обошлись, бюджет и
     * сколько кодов введено верно (verifiedPercent — null, пока ничего не отправлено).
     */
    public static Map<String, Object> usage(CodeLimitStore.Day day, CodeLimitPolicy policy, LocalDate date) {
        Map<String, Object> usage = new LinkedHashMap<>();
        usage.put("date", date.toString());
        usage.put("zone", policy.zone().getId());
        long spent = day.budget().spent();
        Map<String, Object> budget = new LinkedHashMap<>();
        budget.put("spent", spent);
        budget.put("limit", policy.dailyBudget());
        budget.put("alertPercent", policy.budgetAlertPercent());
        budget.put("exhausted", policy.dailyBudget() > 0 && spent >= policy.dailyBudget());
        usage.put("budget", budget);
        long sent = 0;
        for (CodeDelivery delivery : CodeDelivery.values()) {
            usage.put(delivery.name().toLowerCase(Locale.ROOT), Map.of("sent", day.sent(delivery), "cost", day.cost(delivery)));
            sent += day.sent(delivery);
        }
        usage.put("sent", sent);
        usage.put("verified", day.verified());
        usage.put("verifiedPercent", sent == 0 ? null : (int) Math.min(100, Math.round(day.verified() * 100.0 / sent)));
        return usage;
    }

    private static String range(String name, Integer value, int min, int max) {
        if (value == null || value < min || value > max) {
            return name + " — от " + min + " до " + max + ".";
        }
        return null;
    }
}
