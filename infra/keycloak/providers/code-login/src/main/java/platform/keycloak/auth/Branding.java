package platform.keycloak.auth;

import java.util.Base64;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Оформление проекта: название (displayName realm), логотип, цвет и тема страниц входа. Логотип, цвет и тема —
 * атрибуты realm: их меняет админка проекта (через сервис входа) без пересборки темы, страница входа читает их
 * при открытии (GET /realms/{realm}/platform-branding). Здесь — только проверка значений, без Keycloak.
 */
public final class Branding {

    public static final String ATTRIBUTE_LOGO = "platform.branding.logo";
    public static final String ATTRIBUTE_COLOR = "platform.branding.color";
    public static final String ATTRIBUTE_THEME = "platform.branding.theme";

    /** Роль realm служебной учётки сервиса входа: менять оформление (и только его — не весь realm, manage-realm). */
    public static final String MANAGE_ROLE = "manage-branding";

    public static final String THEME_SYSTEM = "system";
    public static final Set<String> THEMES = Set.of(THEME_SYSTEM, "light", "dark");

    public static final int NAME_MAX_LENGTH = 60;

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
        if (branding.color != null && !COLOR.matcher(branding.color).matches()) {
            return "Цвет — в виде #rrggbb (строчные).";
        }
        if (branding.theme == null || !THEMES.contains(branding.theme)) {
            return "Тема — system, light или dark.";
        }
        if (branding.logo != null) {
            Matcher logo = LOGO.matcher(branding.logo);
            if (!logo.matches()) {
                return "Логотип — PNG, JPEG, WebP или SVG (data:image/…;base64,…).";
            }
            try {
                if (Base64.getDecoder().decode(logo.group(2)).length > LOGO_MAX_BYTES) {
                    return "Логотип — не больше 100 КБ.";
                }
            } catch (IllegalArgumentException e) {
                return "Логотип — PNG, JPEG, WebP или SVG (data:image/…;base64,…).";
            }
        }
        return null;
    }
}
