package platform.keycloak.auth;

/**
 * Человек, вошедший через внешний сервис (Яндекс ID, Госуслуги), в понятиях платформы. Телефон — логином (цифрами,
 * как у всех пользователей): по нему вход находит уже существующую учётную запись и предлагает её привязать.
 * Почта и телефон — только подтверждённые у внешнего сервиса; чего нет — null.
 */
public record ExternalUser(String id, String phoneLogin, String email, String firstName, String lastName) {

    /** Логин в Keycloak: телефон цифрами; без телефона — «{провайдер}-{id у провайдера}» (yandex-1000034426). */
    public String username(String providerAlias) {
        return phoneLogin != null ? phoneLogin : providerAlias + "-" + id;
    }
}
