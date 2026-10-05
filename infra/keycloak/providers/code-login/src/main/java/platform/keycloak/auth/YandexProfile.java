package platform.keycloak.auth;

import com.fasterxml.jackson.databind.JsonNode;

/**
 * Профиль Яндекс ID (GET https://login.yandex.ru/info?format=json) → {@link ExternalUser}.
 * Поля: id, default_email (или первый из emails), default_phone.number, first_name, last_name. Телефон и почта —
 * те, что подтверждены в Яндекс ID (неподтверждённые Яндекс не отдаёт). Нужные доступы приложения в Яндексе:
 * login:info, login:email, login:default_phone.
 */
public final class YandexProfile {

    private YandexProfile() {
    }

    public static ExternalUser parse(JsonNode profile) {
        String id = text(profile, "id");
        if (id == null) {
            throw new IllegalArgumentException("Яндекс ID: в профиле нет id");
        }

        String email = text(profile, "default_email");
        JsonNode emails = profile.path("emails");
        if (email == null && emails.isArray() && !emails.isEmpty()) {
            email = emails.get(0).asText(null);
        }

        String phone = PhoneNormalizer.toLogin(text(profile.path("default_phone"), "number")).orElse(null);
        return new ExternalUser(id, phone, email, text(profile, "first_name"), text(profile, "last_name"));
    }

    static String text(JsonNode node, String field) {
        JsonNode value = node.path(field);
        if (value.isMissingNode() || value.isNull()) {
            return null;
        }
        String text = value.asText().trim();
        return text.isEmpty() ? null : text;
    }
}
