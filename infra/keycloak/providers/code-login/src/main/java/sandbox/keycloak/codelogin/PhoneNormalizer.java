package sandbox.keycloak.codelogin;

import java.util.Optional;
import java.util.regex.Pattern;

/**
 * Номер телефона к виду логина: только цифры, без «+» (79990000001).
 * Правила — как PhoneCanonicalizer боевого сервиса: 8XXXXXXXXXX → 7XXXXXXXXXX, 10 цифр → 7 + номер.
 */
public final class PhoneNormalizer {

    // Похоже на номер: цифры, «+», пробелы, скобки, дефисы — и ничего больше.
    private static final Pattern PHONE_LIKE = Pattern.compile("^[+\\d\\s()\\-]+$");
    private static final int MIN_DIGITS = 10;
    private static final int MAX_DIGITS = 15; // E.164

    private PhoneNormalizer() {
    }

    /** Номер в виде логина или пусто, если это не номер (почта, мусор, слишком короткий/длинный). */
    public static Optional<String> toLogin(String input) {
        if (input == null || !PHONE_LIKE.matcher(input.trim()).matches()) {
            return Optional.empty();
        }

        String digits = input.replaceAll("\\D", "");
        if (digits.length() == 11 && digits.charAt(0) == '8') {
            digits = "7" + digits.substring(1);
        } else if (digits.length() == 10) {
            digits = "7" + digits;
        }

        return digits.length() < MIN_DIGITS || digits.length() > MAX_DIGITS ? Optional.empty() : Optional.of(digits);
    }

    /** Что ввели в поле «Телефон или почта» → логин для поиска: номер — цифрами, остальное (почта) — без пробелов по краям. */
    public static String normalizeLogin(String input) {
        if (input == null) {
            return null;
        }
        return toLogin(input).orElse(input.trim());
    }

    /** Для показа и claim phone: «+» и цифры логина. */
    public static String toE164(String login) {
        return "+" + login;
    }
}
