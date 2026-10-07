package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

import java.io.IOException;
import java.io.Reader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.ZoneId;
import java.util.Locale;
import java.util.Map;
import java.util.Properties;

import org.junit.jupiter.api.Test;

class BudgetAlertsTest {

    private static final ZoneId MOSCOW = ZoneId.of("Europe/Moscow");
    private static final Locale RU = Locale.forLanguageTag("ru");

    // Тексты — из темы писем (login-theme), как их получит Keycloak. При сборке образа (infra/keycloak/Dockerfile)
    // темы рядом нет — тогда тексты не проверяются.
    private static Properties messages(String language) throws IOException {
        Properties messages = new Properties();
        Path file = Path.of("../../../../login-theme/src/email/messages/messages_" + language + ".properties");
        assumeTrue(Files.exists(file), "нет темы писем рядом: " + file);
        try (Reader reader = Files.newBufferedReader(file, StandardCharsets.UTF_8)) {
            messages.load(reader);
        }
        return messages;
    }

    private static String plain(String text) {
        return text.replace(' ', ' ').replace(' ', ' ');
    }

    @Test
    void rubles_wholeWithoutKopecks_otherwiseTwoDigits() {
        assertEquals("1 000", plain(BudgetAlerts.rubles(100_000, RU)));
        assertEquals("10,50", BudgetAlerts.rubles(1050, RU));
        assertEquals("0,01", BudgetAlerts.rubles(1, RU));
        assertEquals("1,234.56", BudgetAlerts.rubles(123_456, Locale.ENGLISH));
    }

    @Test
    void threshold_spentOfBudget() throws IOException {
        Map<String, String> texts = BudgetAlerts.texts(messages("ru"), RU, "Демо", 80, 80_400, 100_000, MOSCOW);
        assertEquals("Израсходовано 80 % бюджета на коды", texts.get("budgetTitle"));
        assertEquals("В Демо за сегодня на коды подтверждения (звонки и SMS) ушло 804 ₽ из 1 000 ₽ дневного бюджета.",
            plain(texts.get("budgetText")));
        assertEquals("Когда бюджет кончится, SMS с кодами перестанут уходить до полуночи (часовой пояс Europe/Moscow). "
            + "Звонки и коды на почту продолжат работать.", texts.get("budgetEffect"));
    }

    @Test
    void exhausted_smsOff() throws IOException {
        Map<String, String> texts = BudgetAlerts.texts(messages("ru"), RU, "Демо", 100, 99_850, 100_000, MOSCOW);
        assertEquals("Дневной бюджет на коды исчерпан", texts.get("budgetTitle"));
        assertEquals("В Демо за сегодня на коды подтверждения (звонки и SMS) ушло 998,50 ₽ из 1 000 ₽: на новые SMS бюджета не хватает.",
            plain(texts.get("budgetText")));
        assertEquals("SMS с кодами выключены до полуночи (часовой пояс Europe/Moscow). "
            + "Звонки и коды на почту работают, войти можно и с ключом доступа.", texts.get("budgetEffect"));
    }

    @Test
    void english_numbersByLanguage() throws IOException {
        Map<String, String> texts = BudgetAlerts.texts(messages("en"), Locale.ENGLISH, "Demo", 80, 123_456, 150_000, MOSCOW);
        assertEquals("80% of the code budget spent", texts.get("budgetTitle"));
        assertEquals("Today Demo has spent 1,234.56 ₽ of its 1,500 ₽ daily budget on verification codes (calls and SMS).",
            texts.get("budgetText"));
    }
}
