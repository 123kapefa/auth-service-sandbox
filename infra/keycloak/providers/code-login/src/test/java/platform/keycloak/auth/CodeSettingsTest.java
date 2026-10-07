package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;

import java.time.LocalDate;
import java.util.List;
import java.util.Map;

import org.junit.jupiter.api.Test;

class CodeSettingsTest {

    private static CodeSettings.Representation settings() {
        CodeSettings.Representation settings = new CodeSettings.Representation();
        settings.phoneChannels = List.of("sms", "call");
        settings.countries = List.of("7", "375");
        settings.dailyBudget = 300_000L;
        settings.targetPerHour = 5;
        settings.targetPerDay = 10;
        settings.ipPerHour = 10;
        settings.rangePerHour = 20;
        settings.captchaAfter = 2;
        return settings;
    }

    @Test
    void valid_settings_becomeRealmAttributes_thatThePolicyReads() {
        CodeSettings.Representation settings = settings();
        assertNull(CodeSettings.validate(settings));

        Map<String, String> attributes = CodeSettings.attributes(settings);
        assertEquals("call,sms", attributes.get("phone-channels"));
        assertEquals("7,375", attributes.get("countries"));
        assertEquals("300000", attributes.get("daily-budget"));

        CodeLimitPolicy policy = CodeLimitPolicy.from(attributes::get, CodeLimitPolicy.DEFAULTS);
        assertEquals(List.of(CodeDelivery.CALL, CodeDelivery.SMS), policy.phoneChannels());
        assertEquals(List.of("7", "375"), policy.countries());
        assertEquals(300_000, policy.dailyBudget());
        assertEquals(2, policy.captchaAfter());
        assertEquals(CodeLimitPolicy.DEFAULTS.smsCost(), policy.smsCost());
    }

    @Test
    void view_showsEditableAndServerSettings() {
        Map<String, Object> view = CodeSettings.view(CodeLimitPolicy.DEFAULTS);
        assertEquals(List.of("call", "sms"), view.get("phoneChannels"));
        assertEquals(List.of("7"), view.get("countries"));
        assertEquals(100_000L, view.get("dailyBudget"));
        assertEquals(400L, view.get("smsCost"));
        assertEquals("Europe/Moscow", view.get("zone"));
    }

    @Test
    void onlySms_isValid() {
        CodeSettings.Representation settings = settings();
        settings.phoneChannels = List.of("sms");
        assertNull(CodeSettings.validate(settings));
        settings.dailyBudget = 0L;
        settings.captchaAfter = 0;
        assertNull(CodeSettings.validate(settings));
    }

    @Test
    void wrong_settings_areRejected() {
        assertNotNull(CodeSettings.validate(null));
        CodeSettings.Representation settings = settings();
        settings.phoneChannels = List.of();
        assertNotNull(CodeSettings.validate(settings), "без звонков и SMS");
        settings = settings();
        settings.phoneChannels = List.of("email");
        assertNotNull(CodeSettings.validate(settings));
        settings = settings();
        settings.phoneChannels = List.of("sms", "sms");
        assertNotNull(CodeSettings.validate(settings));
        settings = settings();
        settings.countries = List.of("+7");
        assertNotNull(CodeSettings.validate(settings));
        settings = settings();
        settings.countries = List.of();
        assertNotNull(CodeSettings.validate(settings));
        settings = settings();
        settings.countries = List.of("7", "7");
        assertNotNull(CodeSettings.validate(settings));
        settings = settings();
        settings.dailyBudget = -1L;
        assertNotNull(CodeSettings.validate(settings));
        settings = settings();
        settings.targetPerHour = 0;
        assertNotNull(CodeSettings.validate(settings));
        settings = settings();
        settings.targetPerDay = 3;
        assertNotNull(CodeSettings.validate(settings), "в сутки меньше, чем в час");
        settings = settings();
        settings.ipPerHour = null;
        assertNotNull(CodeSettings.validate(settings));
    }

    @Test
    void day_countsSendsCostsAndVerified() {
        CodeLimitStore.Day day = CodeLimitStore.Day.EMPTY
            .plusSend(CodeDelivery.CALL, 100, 0)
            .plusSend(CodeDelivery.CALL, 100, 0)
            .plusSend(CodeDelivery.SMS, 400, 80)
            .plusSend(CodeDelivery.EMAIL, 0, 0);
        day = new CodeLimitStore.Day(day.budget(), day.sent(), day.cost(), 3);

        CodeLimitStore.Day restored = CodeLimitStore.Day.fromNotes(day.toNotes());
        assertEquals(600, restored.budget().spent());
        assertEquals(80, restored.budget().alertedPercent());
        assertEquals(2, restored.sent(CodeDelivery.CALL));
        assertEquals(200, restored.cost(CodeDelivery.CALL));
        assertEquals(1, restored.sent(CodeDelivery.EMAIL));
        assertEquals(0, restored.cost(CodeDelivery.EMAIL));
        assertEquals(3, restored.verified());

        Map<String, Object> usage = CodeSettings.usage(restored, CodeLimitPolicy.DEFAULTS, LocalDate.of(2026, 10, 7));
        assertEquals("2026-10-07", usage.get("date"));
        assertEquals(Map.of("sent", 1L, "cost", 400L), usage.get("sms"));
        assertEquals(4L, usage.get("sent"));
        assertEquals(75, usage.get("verifiedPercent"));
        @SuppressWarnings("unchecked")
        Map<String, Object> budget = (Map<String, Object>) usage.get("budget");
        assertEquals(600L, budget.get("spent"));
        assertEquals(false, budget.get("exhausted"));
    }

    @Test
    void emptyDay_hasNoVerifiedPercent() {
        CodeLimitStore.Day day = CodeLimitStore.Day.fromNotes(null);
        Map<String, Object> usage = CodeSettings.usage(day, CodeLimitPolicy.DEFAULTS, LocalDate.of(2026, 10, 7));
        assertEquals(0L, usage.get("sent"));
        assertNull(usage.get("verifiedPercent"));
    }

    @Test
    void oldBudgetNotes_stillRead() {
        CodeLimitStore.Day day = CodeLimitStore.Day.fromNotes(Map.of("spent", "1200", "alertedPercent", "80"));
        assertEquals(1200, day.budget().spent());
        assertEquals(0, day.sent(CodeDelivery.SMS));
    }
}
