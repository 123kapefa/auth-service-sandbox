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

    /**
     * Для показа, когда адрес взят из аккаунта, а не введён (вход в два шага, LoginSteps): номер — две последние
     * цифры (у российского — и +7), почта — первая буква и домен.
     */
    public String masked() {
        if (channel == Channel.SMS) {
            String country = value.length() == 11 && value.charAt(0) == '7' ? "7 " : "";
            return "+" + country + "*** ***-**-" + value.substring(value.length() - 2);
        }
        int at = value.indexOf('@');
        return value.charAt(0) + "***" + value.substring(at);
    }
}
