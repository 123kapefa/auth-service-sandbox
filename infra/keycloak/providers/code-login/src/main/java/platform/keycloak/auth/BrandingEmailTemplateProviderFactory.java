package platform.keycloak.auth;

import org.keycloak.Config;
import org.keycloak.email.EmailTemplateProvider;
import org.keycloak.email.EmailTemplateProviderFactory;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;

/**
 * Шаблоны писем с оформлением проекта (BrandingEmailTemplateProvider). order() больше нуля — Keycloak берёт его
 * вместо стандартного freemarker без настройки сервера (spi-email-template-provider).
 */
public final class BrandingEmailTemplateProviderFactory implements EmailTemplateProviderFactory {

    public static final String PROVIDER_ID = "platform-branding";

    @Override
    public EmailTemplateProvider create(KeycloakSession session) {
        return new BrandingEmailTemplateProvider(session);
    }

    @Override
    public void init(Config.Scope config) {
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public int order() {
        return 10;
    }
}
