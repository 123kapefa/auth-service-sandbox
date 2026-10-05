package platform.keycloak.auth;

import java.util.Optional;

import org.keycloak.models.KeycloakSession;

/**
 * Провайдер телефонии: звонок-сброс и SMS. Настоящий провайдер (выбирается до боя) — ещё одна реализация;
 * на стенде — заглушка (HttpPhoneProvider → idp-stub), без настроек — запись в лог (LogPhoneProvider).
 */
interface PhoneProvider {

    /**
     * Звонок-сброс на номер (логин, 79990000001): человеку звонят, отвечать не нужно.
     * Возвращает код — последние 4 цифры номера, с которого звонят; пусто — позвонить не удалось.
     */
    Optional<String> call(KeycloakSession session, String phone);

    /** SMS с кодом; false — отправить не удалось. */
    boolean sms(KeycloakSession session, String phone, String code);

    /** Текст SMS с кодом. */
    static String smsText(String code) {
        return "Код для входа: " + code + ". Никому его не сообщайте.";
    }
}
