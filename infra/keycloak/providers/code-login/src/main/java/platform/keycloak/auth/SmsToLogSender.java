package platform.keycloak.auth;

import org.jboss.logging.Logger;
import org.keycloak.authentication.AuthenticationFlowContext;

/**
 * SMS песочницы: код пишется в лог Keycloak (настоящих SMS в песочнице нет).
 * В проде здесь был бы клиент SMS-провайдера, а код в лог не попадал бы.
 */
final class SmsToLogSender implements CodeSender {

    private static final Logger LOG = Logger.getLogger(SmsToLogSender.class);

    @Override
    public boolean send(AuthenticationFlowContext context, String target, String code) {
        LOG.infof("[DEV SMS] %s: код входа %s", PhoneNormalizer.toE164(target), code);
        return true;
    }
}
