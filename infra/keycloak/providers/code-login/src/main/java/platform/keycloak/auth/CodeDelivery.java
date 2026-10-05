package platform.keycloak.auth;

/**
 * Как код дошёл до человека. На телефон — сначала звонок-сброс (код — последние 4 цифры номера, с которого звонят),
 * SMS — запасной путь; на почту — письмо.
 */
public enum CodeDelivery {
    CALL, SMS, EMAIL;

    /** Ключ в настройке phone-channels («call,sms»). */
    public String key() {
        return name().toLowerCase(java.util.Locale.ROOT);
    }
}
