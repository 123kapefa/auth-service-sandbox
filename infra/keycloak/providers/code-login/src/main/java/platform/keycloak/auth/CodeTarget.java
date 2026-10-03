package platform.keycloak.auth;

import java.util.Locale;
import java.util.Optional;
import java.util.regex.Pattern;

/**
 * Куда отправить код: по введённому в поле «Телефон или почта» — номер (SMS) или адрес (письмо).
 *
 * @param channel канал
 * @param value   номер в виде логина (79990000001) или почта в нижнем регистре
 */
public record CodeTarget(Channel channel, String value) {

    public enum Channel { SMS, EMAIL }

    // Достаточно для выбора канала; настоящую проверку адреса делает письмо с кодом.
    private static final Pattern EMAIL = Pattern.compile("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$");

    public static Optional<CodeTarget> parse(String input) {
        if (input == null) {
            return Optional.empty();
        }
        Optional<String> phone = PhoneNormalizer.toLogin(input);
        if (phone.isPresent()) {
            return Optional.of(new CodeTarget(Channel.SMS, phone.get()));
        }
        String email = input.trim().toLowerCase(Locale.ROOT);
        return EMAIL.matcher(email).matches() ? Optional.of(new CodeTarget(Channel.EMAIL, email)) : Optional.empty();
    }

    /** Для показа: номер — с «+», почта — как есть. */
    public String display() {
        return channel == Channel.SMS ? PhoneNormalizer.toE164(value) : value;
    }
}
