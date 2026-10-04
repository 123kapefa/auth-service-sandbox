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

    private static Branding.AppRepresentation app(String name, String logo, String color) {
        Branding.AppRepresentation branding = new Branding.AppRepresentation();
        branding.name = name;
        branding.logo = logo;
        branding.color = color;
        return branding;
    }

    @Test
    void app_allAsProject_orOwn_valid() {
        assertNull(Branding.validate(app(null, null, null)));
        assertNull(Branding.validate(app("Заказы", png(10), "#1f6feb")));
    }

    @Test
    void app_ownValues_checkedLikeProject() {
        assertEquals("Название — от 1 до 60 символов (или как у проекта).", Branding.validate(app(" ", null, null)));
        assertEquals("Название — от 1 до 60 символов (или как у проекта).", Branding.validate(app("x".repeat(61), null, null)));
        assertEquals("Цвет — в виде #rrggbb (строчные).", Branding.validate(app(null, null, "#FFF")));
        assertEquals("Логотип — не больше 100 КБ.", Branding.validate(app(null, png(Branding.LOGO_MAX_BYTES + 1), null)));
    }

    @Test
    void colorDark_onlyWithColor_andChecked() {
        Branding.Representation project = branding("Магазин", null, "#1f6feb", "system");
        project.colorDark = "#7aa7ff";
        assertNull(Branding.validate(project));
        project.colorDark = "#7AA7FF";
        assertEquals("Цвет для тёмной темы — в виде #rrggbb (строчные).", Branding.validate(project));
        project.colorDark = "#7aa7ff";
        project.color = null;
        assertEquals("Цвет для тёмной темы — только вместе с основным.", Branding.validate(project));

        Branding.AppRepresentation own = app(null, null, null);
        own.colorDark = "#7aa7ff";
        assertEquals("Цвет для тёмной темы — только вместе с основным.", Branding.validate(own));
        own.color = "#1f6feb";
        assertNull(Branding.validate(own));
    }

    @Test
    void decodeLogo_typeAndBytes_orNull() {
        Branding.Image image = Branding.decodeLogo("data:image/svg+xml;base64," + Base64.getEncoder().encodeToString(new byte[] {1, 2}));
        assertEquals("image/svg+xml", image.contentType());
        assertEquals(2, image.bytes().length);
        assertNull(Branding.decodeLogo("data:text/html;base64,AAAA"));
        assertNull(Branding.decodeLogo(null));
    }

    @Test
    void textOn_darkOnLight_lightOnDark() {
        assertEquals("#2e2e2e", Branding.textOn("#ffd400"));
        assertEquals("#f7f7f7", Branding.textOn("#1f3fb0"));
    }
}
