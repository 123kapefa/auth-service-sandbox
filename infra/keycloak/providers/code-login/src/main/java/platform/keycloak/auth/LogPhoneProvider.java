package platform.keycloak.auth;

import java.security.SecureRandom;
import java.util.Optional;

import org.jboss.logging.Logger;
import org.keycloak.models.KeycloakSession;

/**
 * Заглушка стенда: звонки и SMS пишутся в лог Keycloak (настоящих звонков нет). Включается только явно (PHONE_STUB=true,
 * PhoneProviders), потому что код входа в логе — это вход под любым номером для всякого, кто читает логи.
 * В проде вместо неё — клиент провайдера (PHONE_PROVIDER_URL), код в лог не попадает.
 */
final class LogPhoneProvider implements PhoneProvider {

    private static final Logger LOG = Logger.getLogger(LogPhoneProvider.class);
    private final SecureRandom random = new SecureRandom();

    @Override
    public Optional<String> call(KeycloakSession session, String phone) {
        String code = String.format("%04d", random.nextInt(10_000));
        LOG.infof("[DEV CALL] %s: звонок с +7900000%s", PhoneNormalizer.toE164(phone), code);
        return Optional.of(code);
    }

    @Override
    public boolean sms(KeycloakSession session, String phone, String code) {
        LOG.infof("[DEV SMS] %s: код входа %s", PhoneNormalizer.toE164(phone), code);
        return true;
    }

    @Override
    public boolean message(KeycloakSession session, String phone, String text) {
        LOG.infof("[DEV SMS] %s: %s", PhoneNormalizer.toE164(phone), text);
        return true;
    }
}
