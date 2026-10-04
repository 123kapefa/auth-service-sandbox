package platform.keycloak.auth;

import java.util.Base64;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Оформление проекта: название (displayName realm), логотип, цвет и тема страниц входа. Логотип, цвет и тема —
 * атрибуты realm: их меняет админка проекта (через сервис входа) без пересборки темы, страница входа читает их
 * при открытии (GET /realms/{realm}/platform-branding). Приложение может переопределить название, логотип и цвет —
 * атрибуты его клиента (те же имена плюс platform.branding.name); тема — общая для проекта.
 * Здесь — только значения и их проверка, без Keycloak.
 */
public final class Branding {

    public static final String ATTRIBUTE_LOGO = "platform.branding.logo";
    public static final String ATTRIBUTE_COLOR = "platform.branding.color";
    public static final String ATTRIBUTE_THEME = "platform.branding.theme";
    /** Название приложения на его страницах входа и в письмах (у проекта название — displayName realm). */
    public static final String ATTRIBUTE_NAME = "platform.branding.name";

    /** Роль realm служебной учётки сервиса входа: менять оформление (и только его — не весь realm, manage-realm). */
    public static final String MANAGE_ROLE = "manage-branding";

    public static final String THEME_SYSTEM = "system";
    public static final Set<String> THEMES = Set.of(THEME_SYSTEM, "light", "dark");

    public static final int NAME_MAX_LENGTH = 60;

    /** Без своего цвета — серый стиль (strong светлой темы). */
    public static final String GRAY = "#3c3c3c";

    /** Логотип — до 100 КБ: он приходит на каждую страницу входа и хранится в realm. */
    public static final int LOGO_MAX_BYTES = 100 * 1024;

    private static final Pattern COLOR = Pattern.compile("#[0-9a-f]{6}");
    private static final Pattern LOGO = Pattern.compile("data:image/(png|jpeg|webp|svg\\+xml);base64,([A-Za-z0-9+/]+={0,2})");

    private Branding() {
    }

    /** Что прислали и что отдаём: null у логотипа и цвета — нет своего (страницы в сером стиле). */
    public static final class Representation {
        public String name;
        public String logo;
        public String color;
        public String theme;
    }

    /** Своё у приложения: null — как у проекта. */
    public static final class AppRepresentation {
        public String name;
        public String logo;
        public String color;
    }

    /** Ошибка проверки по-русски или null — всё верно. */
    public static String validate(Representation branding) {
        if (branding == null) {
            return "Нет оформления.";
        }
        if (branding.name == null || branding.name.isBlank()) {
            return "Укажите название проекта.";
        }
        if (branding.name.trim().length() > NAME_MAX_LENGTH) {
            return "Название — не длиннее " + NAME_MAX_LENGTH + " символов.";
        }
        if (branding.theme == null || !THEMES.contains(branding.theme)) {
            return "Тема — system, light или dark.";
        }
        return validateLook(branding.logo, branding.color);
    }

    /** Приложение: всё необязательно (null — как у проекта), но заданное — по тем же правилам. */
    public static String validate(AppRepresentation branding) {
        if (branding == null) {
            return "Нет оформления.";
        }
        if (branding.name != null && (branding.name.isBlank() || branding.name.trim().length() > NAME_MAX_LENGTH)) {
            return "Название — от 1 до " + NAME_MAX_LENGTH + " символов (или как у проекта).";
        }
        return validateLook(branding.logo, branding.color);
    }

    private static String validateLook(String logo, String color) {
        if (color != null && !COLOR.matcher(color).matches()) {
            return "Цвет — в виде #rrggbb (строчные).";
        }
        if (logo != null) {
            Matcher matcher = LOGO.matcher(logo);
            if (!matcher.matches()) {
                return "Логотип — PNG, JPEG, WebP или SVG (data:image/…;base64,…).";
            }
            try {
                if (Base64.getDecoder().decode(matcher.group(2)).length > LOGO_MAX_BYTES) {
                    return "Логотип — не больше 100 КБ.";
                }
            } catch (IllegalArgumentException e) {
                return "Логотип — PNG, JPEG, WebP или SVG (data:image/…;base64,…).";
            }
        }
        return null;
    }

    /** Картинка логотипа: тип и байты из data URL (для писем — по ссылке); null — не картинка. */
    public static Image decodeLogo(String logo) {
        if (logo == null) {
            return null;
        }
        Matcher matcher = LOGO.matcher(logo);
        if (!matcher.matches()) {
            return null;
        }
        try {
            return new Image("image/" + matcher.group(1), Base64.getDecoder().decode(matcher.group(2)));
        } catch (IllegalArgumentException e) {
            return null;
        }
    }

    public record Image(String contentType, byte[] bytes) {
    }

    /**
     * Цвет текста на заливке цветом проекта: тёмный на светлом, светлый на тёмном (яркость WCAG) — как на страницах
     * входа и в админке.
     */
    public static String textOn(String color) {
        double luminance = 0.2126 * linear(color, 1) + 0.7152 * linear(color, 3) + 0.0722 * linear(color, 5);
        // Контраст с #f7f7f7 и с #2e2e2e равен при яркости ≈ 0.22: выше — тёмный текст.
        return luminance > 0.22 ? "#2e2e2e" : "#f7f7f7";
    }

    private static double linear(String color, int at) {
        double channel = Integer.parseInt(color.substring(at, at + 2), 16) / 255.0;
        return channel <= 0.03928 ? channel / 12.92 : Math.pow((channel + 0.055) / 1.055, 2.4);
    }
}
