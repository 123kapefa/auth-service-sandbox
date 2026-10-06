package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;
import java.time.format.DateTimeParseException;
import java.util.Optional;
import java.util.function.Predicate;

/**
 * Правила смены номера самим человеком (10.5), без Keycloak: не чаще раза в сутки, номер — настоящий, другой и ничей.
 */
final class PhoneChangeRules {

    /** Когда номер меняли в последний раз (ISO-время); пишет только UpdatePhoneAction. */
    static final String CHANGED_AT = "phone_changed_at";
    /** Прежний номер («+79990000001») — для журнала сервиса и поддержки. */
    static final String PREVIOUS = "phone_previous";

    static final Duration INTERVAL = Duration.ofDays(1);

    private PhoneChangeRules() {
    }

    /** Итог проверки нового номера. */
    sealed interface Check {
        /** Можно: login — номер цифрами (79990000001). */
        record Ok(String login) implements Check {
        }

        /** Не номер (или короткий, или длинный). */
        record Invalid() implements Check {
        }

        /** Тот же номер, что сейчас. */
        record Same() implements Check {
        }

        /** Номер уже у другого аккаунта. */
        record Taken() implements Check {
        }

        /** Меняли меньше суток назад: снова — с nextAllowed. */
        record TooOften(Instant nextAllowed) implements Check {
        }
    }

    /** Когда снова можно менять; пусто — уже можно (или ещё не меняли, или время не разобрать). */
    static Optional<Instant> nextAllowed(String changedAt, Instant now) {
        if (changedAt == null || changedAt.isBlank()) {
            return Optional.empty();
        }
        try {
            Instant next = Instant.parse(changedAt).plus(INTERVAL);
            return now.isBefore(next) ? Optional.of(next) : Optional.empty();
        } catch (DateTimeParseException e) {
            return Optional.empty();
        }
    }

    /**
     * @param input     что ввели
     * @param current   нынешний номер цифрами (null — номера нет)
     * @param changedAt атрибут phone_changed_at
     * @param taken     номер цифрами занят другим аккаунтом
     */
    static Check check(String input, String current, String changedAt, Instant now, Predicate<String> taken) {
        Optional<Instant> next = nextAllowed(changedAt, now);
        if (next.isPresent()) {
            return new Check.TooOften(next.get());
        }
        Optional<String> login = PhoneNormalizer.toLogin(input);
        if (login.isEmpty()) {
            return new Check.Invalid();
        }
        if (login.get().equals(current)) {
            return new Check.Same();
        }
        if (taken.test(login.get())) {
            return new Check.Taken();
        }
        return new Check.Ok(login.get());
    }
}
