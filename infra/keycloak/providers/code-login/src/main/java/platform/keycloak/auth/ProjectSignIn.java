package platform.keycloak.auth;

import java.time.Instant;
import java.time.LocalDate;
import java.util.EnumMap;
import java.util.EnumSet;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.function.Function;
import java.util.function.Predicate;

/**
 * Способы входа проекта (10.7): админ проекта решает, что вообще есть у людей проекта — вход по телефону (номер как
 * логин и код на него), по почте (почта как логин и код на неё), пароль, ключ доступа, Яндекс ID, Госуслуги — и
 * обязательна ли 2FA. Здесь — значения, правила и проверка, без Keycloak (Keycloak — ProjectSignInRealm).
 * <ul>
 *   <li>Выключенный способ не работает ни у кого: ни вход, ни регистрация.</li>
 *   <li>Переходный период до даты (телефон, почта, пароль): способ работает только у тех, кто был в проекте до начала
 *       периода и входит только им; при входе им предлагают добавить ключ доступа. Новые люди им не регистрируются.
 *       После даты способ выключен.</li>
 *   <li>Ключ доступа, Яндекс ID и Госуслуги выключаются сразу: их выключают переключатели самого Keycloak
 *       (ключи доступа realm, внешний вход).</li>
 *   <li>Хотя бы один способ регистрации — код на телефон, код на почту, Яндекс ID или Госуслуги — включён всегда.</li>
 * </ul>
 */
public final class ProjectSignIn {

    /** Атрибуты realm: platform.sign-in.&lt;способ&gt; — on, off, transition:&lt;до даты&gt;:&lt;с, мс&gt;; two-factor. */
    static final String ATTRIBUTE_PREFIX = "platform.sign-in.";
    static final String TWO_FACTOR = "two-factor";

    /** Переходный период — не дольше года. */
    static final int TRANSITION_MAX_DAYS = 366;

    enum Method {
        PHONE, EMAIL, PASSWORD, PASSKEY, YANDEX, ESIA;

        String key() {
            return name().toLowerCase(Locale.ROOT);
        }

        static Method of(String key) {
            for (Method method : values()) {
                if (method.key().equals(key)) {
                    return method;
                }
            }
            return null;
        }
    }

    /** Способы, которыми новые люди регистрируются. */
    static final Set<Method> REGISTRATION = EnumSet.of(Method.PHONE, Method.EMAIL, Method.YANDEX, Method.ESIA);
    /** Способы с переходным периодом — их проверяет плагин у каждого человека. */
    static final Set<Method> TRANSITIONAL = EnumSet.of(Method.PHONE, Method.EMAIL, Method.PASSWORD);
    /** Внешний вход: способ = вход через Keycloak identity provider с таким alias. */
    static final Set<Method> EXTERNAL = EnumSet.of(Method.YANDEX, Method.ESIA);

    sealed interface Status {
        record On() implements Status { }

        record Off() implements Status { }

        /** До until включительно работает у тех, кто был до since и входит только этим способом. */
        record Transition(LocalDate until, Instant since) implements Status { }

        Status ON = new On();
        Status OFF = new Off();
    }

    /**
     * Настройки проекта.
     *
     * @param methods           состояние каждого способа (нет записи — включён)
     * @param twoFactorRequired 2FA обязательна для всех (иначе — по желанию)
     */
    record Settings(Map<Method, Status> methods, boolean twoFactorRequired) {

        static final Settings ALL_ON = new Settings(Map.of(), false);

        Settings {
            methods = methods.isEmpty() ? Map.of() : Map.copyOf(new EnumMap<>(methods));
        }

        Status status(Method method) {
            return methods.getOrDefault(method, Status.ON);
        }

        /** Состояние на сегодня: переходный период кончился — выключен. */
        Status effective(Method method, LocalDate today) {
            Status status = status(method);
            return status instanceof Status.Transition transition && today.isAfter(transition.until()) ? Status.OFF : status;
        }

        boolean on(Method method, LocalDate today) {
            return effective(method, today) instanceof Status.On;
        }

        /** Всё включено, 2FA по желанию — проверять у людей нечего. */
        boolean allOn() {
            return !twoFactorRequired && methods.values().stream().allMatch(Status.On.class::isInstance);
        }
    }

    /**
     * Что есть у человека.
     *
     * @param hasPhone    есть номер (логин — номер)
     * @param hasEmail    есть почта
     * @param hasPassword задан пароль
     * @param hasPasskey  есть ключ доступа
     * @param linked      привязанный внешний вход (YANDEX, ESIA)
     * @param disabled    что человек выключил у себя (SignInMethods, с учётом страховки)
     * @param created     когда появился в проекте
     */
    record User(boolean hasPhone, boolean hasEmail, boolean hasPassword, boolean hasPasskey, Set<Method> linked,
                Set<String> disabled, Instant created) {
    }

    // --- атрибуты realm ---

    static Settings read(Function<String, String> attribute) {
        Map<Method, Status> methods = new EnumMap<>(Method.class);
        for (Method method : Method.values()) {
            methods.put(method, parse(attribute.apply(ATTRIBUTE_PREFIX + method.key())));
        }
        return new Settings(methods, "required".equals(attribute.apply(ATTRIBUTE_PREFIX + TWO_FACTOR)));
    }

    /** Пусто или непонятно — включён: неверная запись не должна запереть проект. */
    static Status parse(String value) {
        if (value == null || value.isBlank() || "on".equals(value)) {
            return Status.ON;
        }
        if ("off".equals(value)) {
            return Status.OFF;
        }
        String[] parts = value.split(":");
        if (parts.length == 3 && "transition".equals(parts[0])) {
            try {
                return new Status.Transition(LocalDate.parse(parts[1]), Instant.ofEpochMilli(Long.parseLong(parts[2])));
            } catch (RuntimeException e) {
                return Status.ON;
            }
        }
        return Status.ON;
    }

    static String format(Status status) {
        return switch (status) {
            case Status.On ignored -> "on";
            case Status.Off ignored -> "off";
            case Status.Transition transition -> "transition:" + transition.until() + ":" + transition.since().toEpochMilli();
        };
    }

    // --- люди ---

    /** Чем человек может войти, если включено то, что on. */
    static Set<Method> ways(User user, Predicate<Method> on) {
        Set<Method> ways = EnumSet.noneOf(Method.class);
        if (on.test(Method.PHONE) && user.hasPhone() && !user.disabled().contains(SignInMethods.PHONE_CODE)) {
            ways.add(Method.PHONE);
        }
        if (on.test(Method.EMAIL) && user.hasEmail() && !user.disabled().contains(SignInMethods.EMAIL_CODE)) {
            ways.add(Method.EMAIL);
        }
        // Пароль вводят после номера или почты: без включённого логина он бесполезен.
        boolean login = (on.test(Method.PHONE) && user.hasPhone()) || (on.test(Method.EMAIL) && user.hasEmail());
        if (on.test(Method.PASSWORD) && user.hasPassword() && login && !user.disabled().contains(SignInMethods.PASSWORD)) {
            ways.add(Method.PASSWORD);
        }
        if (on.test(Method.PASSKEY) && user.hasPasskey()) {
            ways.add(Method.PASSKEY);
        }
        for (Method external : EXTERNAL) {
            if (on.test(external) && user.linked().contains(external)) {
                ways.add(external);
            }
        }
        return ways;
    }

    /** Человек входит только этим способом: с ним вход есть, без него — нет (остальное — как в on). */
    static boolean onlyBy(User user, Method method, Predicate<Method> on) {
        return !ways(user, on.or(method::equals)).isEmpty() && ways(user, on.and(other -> other != method)).isEmpty();
    }

    /**
     * Какие способы человеку недоступны: выключенные в проекте и переходные, если человек не из тех, кому период
     * оставлен (был до начала и входит только этим способом).
     */
    static Set<Method> blocked(Settings settings, User user, LocalDate today) {
        Set<Method> blocked = EnumSet.noneOf(Method.class);
        Predicate<Method> on = method -> settings.on(method, today);
        for (Method method : Method.values()) {
            Status status = settings.effective(method, today);
            if (status instanceof Status.Off) {
                blocked.add(method);
            } else if (status instanceof Status.Transition transition
                && !(user.created().isBefore(transition.since()) && onlyBy(user, method, on))) {
                blocked.add(method);
            }
        }
        return blocked;
    }

    /** Способы в переходном периоде, которые человеку оставлены (ему — предложить добавить другой). */
    static Set<Method> transitional(Settings settings, User user, LocalDate today) {
        Set<Method> kept = EnumSet.noneOf(Method.class);
        for (Method method : Method.values()) {
            if (settings.effective(method, today) instanceof Status.Transition && !blocked(settings, user, today).contains(method)) {
                kept.add(method);
            }
        }
        return kept;
    }

    // --- API (SignInSettingsResource) ---

    /** Что присылает админка: все способы целиком; status — on, off или transition (с until — yyyy-MM-dd). */
    public static final class Request {
        public Map<String, MethodRequest> methods;
        public String twoFactor;
    }

    public static final class MethodRequest {
        public String status;
        public String until;
    }

    /**
     * Настройки из запроса или ошибка по-русски. Начало переходного периода — now, если способ только что переводят
     * в него, иначе — прежнее (продлить период можно, а «старыми» новые люди от этого не станут).
     */
    static Object fromRequest(Request request, Settings current, Instant now) {
        if (request == null || request.methods == null) {
            return "Нет настроек.";
        }
        Map<Method, Status> methods = new EnumMap<>(Method.class);
        for (Method method : Method.values()) {
            MethodRequest value = request.methods.get(method.key());
            if (value == null || value.status == null) {
                return "Укажите, включён ли способ: " + method.key() + ".";
            }
            switch (value.status) {
                case "on" -> methods.put(method, Status.ON);
                case "off" -> methods.put(method, Status.OFF);
                case "transition" -> {
                    LocalDate until;
                    try {
                        until = LocalDate.parse(value.until == null ? "" : value.until);
                    } catch (RuntimeException e) {
                        return "Дата конца переходного периода — yyyy-MM-dd: " + method.key() + ".";
                    }
                    Instant since = current.status(method) instanceof Status.Transition old ? old.since() : now;
                    methods.put(method, new Status.Transition(until, since));
                }
                default -> {
                    return "Способ — on, off или transition: " + method.key() + ".";
                }
            }
        }
        if (!"optional".equals(request.twoFactor) && !"required".equals(request.twoFactor)) {
            return "2FA — optional или required.";
        }
        return new Settings(methods, "required".equals(request.twoFactor));
    }

    /** Настройки для админки: состояние способов (переходный — с датами), настроен ли внешний вход, 2FA. */
    static Map<String, Object> view(Settings settings, Set<Method> configured, LocalDate today) {
        Map<String, Object> methods = new java.util.LinkedHashMap<>();
        for (Method method : Method.values()) {
            Map<String, Object> value = new java.util.LinkedHashMap<>();
            Status status = settings.effective(method, today);
            switch (status) {
                case Status.On ignored -> value.put("status", "on");
                case Status.Off ignored -> value.put("status", "off");
                case Status.Transition transition -> {
                    value.put("status", "transition");
                    value.put("until", transition.until().toString());
                    value.put("since", transition.since().toString());
                }
            }
            if (EXTERNAL.contains(method)) {
                value.put("configured", configured.contains(method));
            }
            methods.put(method.key(), value);
        }
        Map<String, Object> view = new java.util.LinkedHashMap<>();
        view.put("methods", methods);
        view.put("twoFactor", settings.twoFactorRequired() ? "required" : "optional");
        return view;
    }

    // --- проверка новых настроек ---

    /**
     * Ошибка по-русски или null. configured — внешний вход, который настроен в Keycloak (без ключей не включить).
     */
    static String validate(Settings next, Set<Method> configured, LocalDate today) {
        for (Method method : EXTERNAL) {
            if (!configured.contains(method) && !(next.status(method) instanceof Status.Off)) {
                return method == Method.YANDEX
                    ? "Яндекс ID не настроен в Keycloak: сначала ключи проекта (enable-external-login.py)."
                    : "Госуслуги не настроены в Keycloak: сначала ключи проекта (enable-external-login.py).";
            }
        }
        for (Method method : Method.values()) {
            if (next.status(method) instanceof Status.Transition transition) {
                if (!TRANSITIONAL.contains(method)) {
                    return "Переходный период — только у входа по телефону, по почте и пароля.";
                }
                if (!transition.until().isAfter(today) || transition.until().isAfter(today.plusDays(TRANSITION_MAX_DAYS))) {
                    return "Переходный период — до даты в будущем, не дальше года.";
                }
            }
        }
        if (REGISTRATION.stream().noneMatch(method -> next.status(method) instanceof Status.On)) {
            return "Нужен хотя бы один способ регистрации: телефон, почта, Яндекс ID или Госуслуги.";
        }
        return null;
    }

    private ProjectSignIn() {
    }
}
