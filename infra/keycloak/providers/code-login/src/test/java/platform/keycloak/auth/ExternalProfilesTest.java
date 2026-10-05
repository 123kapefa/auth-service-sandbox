package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import org.junit.jupiter.api.Test;

class ExternalProfilesTest {

    private static final ObjectMapper JSON = new ObjectMapper();

    private static JsonNode json(String text) throws Exception {
        return JSON.readTree(text.replace('\'', '"'));
    }

    @Test
    void yandex_fullProfile() throws Exception {
        ExternalUser user = YandexProfile.parse(json("""
            {'id': '1000034426', 'login': 'ivan', 'default_email': 'ivan@yandex.ru', 'emails': ['ivan@yandex.ru'],
             'first_name': 'Иван', 'last_name': 'Петров', 'default_phone': {'id': 12345678, 'number': '+79037659418'}}"""));
        assertEquals("1000034426", user.id());
        assertEquals("79037659418", user.phoneLogin());
        assertEquals("ivan@yandex.ru", user.email());
        assertEquals("Иван", user.firstName());
        assertEquals("Петров", user.lastName());
        assertEquals("79037659418", user.username("yandex"));
    }

    @Test
    void yandex_withoutPhone_usernameFromId_emailFromList() throws Exception {
        ExternalUser user = YandexProfile.parse(json("{'id': 42, 'emails': ['a@ya.ru', 'b@ya.ru']}"));
        assertNull(user.phoneLogin());
        assertEquals("a@ya.ru", user.email());
        assertEquals("yandex-42", user.username("yandex"));
    }

    @Test
    void yandex_withoutId_isRejected() {
        assertThrows(IllegalArgumentException.class, () -> YandexProfile.parse(json("{'login': 'x'}")));
    }

    @Test
    void esia_onlyVerifiedContacts() throws Exception {
        JsonNode person = json("{'firstName': 'Мария', 'lastName': 'Сидорова', 'middleName': 'Ивановна', 'trusted': true}");
        JsonNode contacts = json("""
            {'elements': [
              {'type': 'MBT', 'value': '+7(999)1112233', 'vrfStu': 'NOT_VERIFIED'},
              {'type': 'EML', 'value': 'old@mail.ru', 'vrfStu': 'NOT_VERIFIED'},
              {'type': 'MBT', 'value': '+7(999)0000101', 'vrfStu': 'VERIFIED'},
              {'type': 'EML', 'value': 'maria@mail.ru', 'vrfStu': 'VERIFIED'}]}""");
        ExternalUser user = EsiaProfile.parse("1000299654", person, contacts);
        assertEquals("79990000101", user.phoneLogin());
        assertEquals("maria@mail.ru", user.email());
        assertEquals("Мария", user.firstName());
        assertEquals("Сидорова", user.lastName());
    }

    @Test
    void esia_noVerifiedContacts_usernameFromOid() throws Exception {
        ExternalUser user = EsiaProfile.parse("1000299655", json("{}"),
            json("{'elements': [{'type': 'MBT', 'value': '+7(999)1112233', 'vrfStu': 'NOT_VERIFIED'}]}"));
        assertNull(user.phoneLogin());
        assertNull(user.email());
        assertEquals("esia-1000299655", user.username("esia"));
    }
}
