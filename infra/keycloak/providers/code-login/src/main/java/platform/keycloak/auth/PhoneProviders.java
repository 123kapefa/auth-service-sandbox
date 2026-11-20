package platform.keycloak.auth;

/**
 * Выбор телефонии по настройкам плагина. Боевой провайдер (адрес) главнее заглушки; запись кодов в лог (LogPhoneProvider)
 * — только по явному флагу стенда; иначе телефонии нет (NoPhoneProvider): код входа не должен оказаться в логе из-за
 * того, что про настройку забыли.
 */
final class PhoneProviders {

    private PhoneProviders() {
    }

    /** @param url адрес провайдера (PHONE_PROVIDER_URL), может быть пустым; @param logStub PHONE_STUB=true — коды в лог (стенд). */
    static PhoneProvider choose(String url, boolean logStub) {
        if (url != null && !url.isBlank()) {
            return new HttpPhoneProvider(url);
        }
        return logStub ? new LogPhoneProvider() : new NoPhoneProvider();
    }
}
