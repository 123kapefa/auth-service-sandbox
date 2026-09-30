package sandbox.keycloak.codelogin;

import org.jboss.logging.Logger;

/**
 * SMS песочницы: код пишется в лог Keycloak (настоящих SMS в песочнице нет).
 * В проде здесь был бы клиент SMS-провайдера, а код в лог не попадал бы.
 */
final class SmsToLogSender implements CodeSender {

    private static final Logger LOG = Logger.getLogger(SmsToLogSender.class);

    @Override
    public void send(String target, String code) {
        LOG.infof("[SANDBOX SMS] %s: код входа %s", PhoneNormalizer.toE164(target), code);
    }
}
