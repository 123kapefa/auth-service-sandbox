package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import java.util.Base64;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class BrandingTest {

    private static Branding.Representation branding(String name, String logo, String color, String theme) {
        Branding.Representation branding = new Branding.Representation();
        branding.name = name;
        branding.logo = logo;
        branding.color = color;
        branding.theme = theme;
        return branding;
    }

    private static String png(int bytes) {
        return "data:image/png;base64," + Base64.getEncoder().encodeToString(new byte[bytes]);
    }

    @Test
    void full_andEmpty_areValid() {
        assertNull(Branding.validate(branding("Магазин", png(10), "#1f6feb", "dark")));
        assertNull(Branding.validate(branding("Магазин", null, null, "system")));
    }

    @ParameterizedTest(name = "цвет «{0}» не принят")
    @ValueSource(strings = {"red", "#fff", "#1F6FEB", "1f6feb", "#1f6feb00", "url(x)"})
    void badColor(String color) {
        assertEquals("Цвет — в виде #rrggbb (строчные).", Branding.validate(branding("Магазин", null, color, "system")));
    }

    @ParameterizedTest(name = "логотип «{0}» не принят")
    @ValueSource(strings = {"https://evil.example/logo.png", "data:image/gif;base64,AAAA", "data:text/html;base64,AAAA",
        "data:image/png;base64,***", "data:image/png,plain"})
    void badLogo(String logo) {
        assertEquals("Логотип — PNG, JPEG, WebP или SVG (data:image/…;base64,…).",
            Branding.validate(branding("Магазин", logo, null, "system")));
    }

    @Test
    void logo_over100Kb_rejected() {
        assertNull(Branding.validate(branding("Магазин", png(Branding.LOGO_MAX_BYTES), null, "light")));
        assertEquals("Логотип — не больше 100 КБ.", Branding.validate(branding("Магазин", png(Branding.LOGO_MAX_BYTES + 1), null, "light")));
    }

    @Test
    void name_andTheme_required() {
        assertEquals("Укажите название проекта.", Branding.validate(branding("  ", null, null, "system")));
        assertEquals("Название — не длиннее 60 символов.", Branding.validate(branding("x".repeat(61), null, null, "system")));
        assertEquals("Тема — system, light или dark.", Branding.validate(branding("Магазин", null, null, "blue")));
        assertEquals("Тема — system, light или dark.", Branding.validate(branding("Магазин", null, null, null)));
    }
}
