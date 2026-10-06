package platform.keycloak.auth;

import java.util.List;
import java.util.Map;

import org.jboss.logging.Logger;
import org.keycloak.email.EmailException;
import org.keycloak.email.freemarker.FreeMarkerEmailTemplateProvider;
import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;

/**
 * Письма Keycloak в оформлении проекта и приложения: шаблону (html/template.ftl темы писем) — branding (название,
 * логотип, цвет), в теме письма и тексте — название приложения, если письмо из его входа (сброс пароля, подтверждение
 * почты), иначе проекта. Остальное — как у стандартного FreeMarkerEmailTemplateProvider.
 *
 * «Забыли пароль» при выключенном входе по паролю (SignInMethods) письмо не шлёт: ссылка включила бы пароль обратно.
 * Страница при этом та же, что после отправки (как и для незнакомой почты), — по ней не понять, что выключено.
 */
public final class BrandingEmailTemplateProvider extends FreeMarkerEmailTemplateProvider {

    private static final Logger LOG = Logger.getLogger(BrandingEmailTemplateProvider.class);

    public BrandingEmailTemplateProvider(KeycloakSession session) {
        super(session);
    }

    @Override
    public void sendPasswordReset(String link, long expirationInMinutes) throws EmailException {
        if (user != null && SignInMethods.isDisabled(session, realm, user, SignInMethods.PASSWORD)) {
            LOG.infof("Письмо «Забыли пароль» для %s не отправлено: вход по паролю выключен", user.getUsername());
            return;
        }
        super.sendPasswordReset(link, expirationInMinutes);
    }

    @Override
    protected String getRealmName() {
        return (String) BrandingResolver.effective(realm, app()).get("name");
    }

    @Override
    protected EmailTemplate processTemplate(
        String subjectKey, List<Object> subjectAttributes, String template, Map<String, Object> attributes)
        throws EmailException {
        attributes.put("branding", BrandingResolver.forEmail(session, realm, app()));
        return super.processTemplate(subjectKey, subjectAttributes, template, attributes);
    }

    private ClientModel app() {
        return BrandingResolver.app(authenticationSession);
    }
}
