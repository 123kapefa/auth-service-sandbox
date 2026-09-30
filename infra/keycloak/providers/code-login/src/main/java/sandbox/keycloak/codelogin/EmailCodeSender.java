package sandbox.keycloak.codelogin;

import java.io.IOException;
import java.text.MessageFormat;
import java.util.Locale;
import java.util.Properties;

import org.jboss.logging.Logger;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailSenderProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.theme.Theme;

/**
 * Код письмом через SMTP realm (в песочнице — Mailpit). Шаблоны писем Keycloak (EmailTemplateProvider) ждут
 * пользователя, а неизвестной почте код уходит до его создания — поэтому письмо собирается здесь,
 * тексты — из сообщений темы входа (sandboxCodeEmail*), на языке страницы входа.
 */
final class EmailCodeSender implements CodeSender {

    private static final Logger LOG = Logger.getLogger(EmailCodeSender.class);

    private final long ttlMinutes;

    EmailCodeSender(CodePolicy policy) {
        this.ttlMinutes = policy.ttl().toMinutes();
    }

    @Override
    public boolean send(AuthenticationFlowContext context, String target, String code) {
        KeycloakSession session = context.getSession();
        RealmModel realm = context.getRealm();
        try {
            Locale locale = session.getContext().resolveLocale(null);
            Properties messages = session.theme().getTheme(Theme.Type.LOGIN).getEnhancedMessages(realm, locale);
            String subject = format(messages, locale, "sandboxCodeEmailSubject");
            String body = format(messages, locale, "sandboxCodeEmailBody", code, ttlMinutes);

            session.getProvider(EmailSenderProvider.class).send(realm.getSmtpConfig(), target, subject, body, null);
            return true;
        } catch (EmailException | IOException e) {
            LOG.warnf(e, "Не удалось отправить код на %s", target);
            return false;
        }
    }

    private static String format(Properties messages, Locale locale, String key, Object... parameters) {
        return new MessageFormat(messages.getProperty(key, key), locale).format(parameters);
    }
}
