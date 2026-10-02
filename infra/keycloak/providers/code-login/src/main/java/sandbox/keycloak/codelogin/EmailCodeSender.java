package sandbox.keycloak.codelogin;

import java.io.IOException;
import java.text.MessageFormat;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Properties;

import org.jboss.logging.Logger;
import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailSenderProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.theme.FreeMarkerException;
import org.keycloak.theme.Theme;
import org.keycloak.theme.beans.MessageFormatterMethod;
import org.keycloak.theme.freemarker.FreeMarkerProvider;

/**
 * Код письмом через SMTP realm (в песочнице — Mailpit). Письмо — шаблон темы писем realm (sandbox-login-code.ftl,
 * html и text), как у писем Keycloak. Их EmailTemplateProvider ждёт пользователя, а неизвестной почте код уходит
 * до его создания — поэтому шаблон обрабатывается здесь, на языке страницы входа.
 */
final class EmailCodeSender implements CodeSender {

    private static final Logger LOG = Logger.getLogger(EmailCodeSender.class);
    private static final String TEMPLATE = "sandbox-login-code.ftl";

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
            Theme theme = session.theme().getTheme(Theme.Type.EMAIL);
            Properties messages = theme.getEnhancedMessages(realm, locale);
            String realmName = realm.getDisplayName() == null || realm.getDisplayName().isBlank()
                ? realm.getName()
                : realm.getDisplayName();

            Map<String, Object> attributes = new HashMap<>();
            attributes.put("locale", locale);
            attributes.put("realmName", realmName);
            attributes.put("msg", new MessageFormatterMethod(locale, messages));
            attributes.put("code", code);
            attributes.put("ttlMinutes", ttlMinutes);

            FreeMarkerProvider freeMarker = session.getProvider(FreeMarkerProvider.class);
            String html = freeMarker.processTemplate(attributes, "html/" + TEMPLATE, theme);
            String text = freeMarker.processTemplate(attributes, "text/" + TEMPLATE, theme);
            String subject = new MessageFormat(messages.getProperty("sandboxCodeEmailSubject", "sandboxCodeEmailSubject"), locale)
                .format(new Object[] {realmName});

            session.getProvider(EmailSenderProvider.class).send(realm.getSmtpConfig(), target, subject, text, html);
            return true;
        } catch (EmailException | IOException | FreeMarkerException e) {
            LOG.warnf(e, "Не удалось отправить код на %s", target);
            return false;
        }
    }
}
