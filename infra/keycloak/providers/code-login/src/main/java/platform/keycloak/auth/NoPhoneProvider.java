package platform.keycloak.auth;

import java.util.Optional;

import org.jboss.logging.Logger;
import org.keycloak.models.KeycloakSession;

/**
 * Телефонии нет (не задан ни провайдер, ни явная заглушка стенда): звонки и SMS не отправляются, коды в лог не попадают.
 * Вход по коду на почту и остальное работают; на номер человек видит «не удалось отправить код». Номер и код в
 * сообщение не пишутся — в логе только причина.
 */
final class NoPhoneProvider implements PhoneProvider {

    private static final Logger LOG = Logger.getLogger(NoPhoneProvider.class);

    @Override
    public boolean available() {
        return false;
    }

    @Override
    public Optional<String> call(KeycloakSession session, String phone) {
        warn("звонок");
        return Optional.empty();
    }

    @Override
    public boolean sms(KeycloakSession session, String phone, String code) {
        warn("SMS с кодом");
        return false;
    }

    @Override
    public boolean message(KeycloakSession session, String phone, String text) {
        warn("SMS-уведомление");
        return false;
    }

    private static void warn(String what) {
        LOG.warnf("Телефония не настроена, %s не отправлено: задайте PHONE_PROVIDER_URL (боевой провайдер) или KC_SPI_AUTHENTICATOR__PLATFORM_CODE_LOGIN__PHONE_STUB=true (только стенд)", what);
    }
}
