package platform.keycloak.auth;

import com.fasterxml.jackson.databind.JsonNode;

/**
 * Данные Госуслуг (ЕСИА, REST API) → {@link ExternalUser}.
 * Человек: GET /rs/prns/{oid} — firstName, lastName. Контакты: GET /rs/prns/{oid}/ctts?embed=(elements) — elements
 * с type (MBT — мобильный, EML — почта), value и vrfStu. Берутся только подтверждённые (vrfStu = VERIFIED): по
 * телефону вход находит существующую учётную запись, неподтверждённому номеру верить нельзя.
 */
public final class EsiaProfile {

    static final String MOBILE = "MBT";
    static final String EMAIL = "EML";
    static final String VERIFIED = "VERIFIED";

    private EsiaProfile() {
    }

    public static ExternalUser parse(String oid, JsonNode person, JsonNode contacts) {
        String phone = null;
        String email = null;
        for (JsonNode contact : contacts.path("elements")) {
            if (!VERIFIED.equals(YandexProfile.text(contact, "vrfStu"))) {
                continue;
            }
            String type = YandexProfile.text(contact, "type");
            String value = YandexProfile.text(contact, "value");
            if (MOBILE.equals(type) && phone == null) {
                phone = PhoneNormalizer.toLogin(value).orElse(null);
            } else if (EMAIL.equals(type) && email == null) {
                email = value;
            }
        }
        return new ExternalUser(oid, phone, email, YandexProfile.text(person, "firstName"), YandexProfile.text(person, "lastName"));
    }
}
