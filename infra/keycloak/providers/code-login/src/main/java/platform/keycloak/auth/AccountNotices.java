package platform.keycloak.auth;

import java.io.IOException;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Properties;

import org.keycloak.authentication.Authenticator;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailTemplateProvider;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.theme.Theme;

/**
 * Уведомления человеку об изменениях в аккаунте: письмо в оформлении проекта (шаблон с атрибутами noticeText и
 * noticeNotYou) и SMS через телефонию кодов. Тексты — в messages_*.properties темы писем.
 */
final class AccountNotices {

    private AccountNotices() {
    }

    /** Тексты темы писем на языке человека. */
    static Properties messages(KeycloakSession session, RealmModel realm, UserModel user) throws IOException {
        return session.theme().getTheme(Theme.Type.EMAIL)
            .getEnhancedMessages(realm, session.getContext().resolveLocale(user));
    }

    /** Название проекта (оформление 6б). */
    static String realmName(RealmModel realm) {
        return (String) BrandingResolver.effective(realm, null).get("name");
    }

    /** Письмо, если у человека есть почта; false — почты нет. */
    static boolean email(KeycloakSession session, RealmModel realm, UserModel user, String subjectKey, String template,
                         String text, String notYou) throws EmailException {
        if (user.getEmail() == null || user.getEmail().isBlank()) {
            return false;
        }
        Map<String, Object> attributes = new HashMap<>();
        attributes.put("noticeText", text);
        attributes.put("noticeNotYou", notYou);
        session.getProvider(EmailTemplateProvider.class).setRealm(realm).setUser(user)
            .send(subjectKey, List.of(), template, attributes);
        return true;
    }

    /** Номер для SMS: атрибут phone («+79990000001») или логин-номер; null — номера нет. */
    static String phone(UserModel user) {
        String phone = user.getFirstAttribute("phone");
        if (phone != null && !phone.isBlank()) {
            return PhoneNormalizer.normalizeLogin(phone);
        }
        return user.getUsername().matches("\\d{10,15}") ? user.getUsername() : null;
    }

    /** Телефония кодов (настройки плагина входа по коду). */
    static PhoneProvider phoneProvider(KeycloakSession session) {
        Object factory = session.getKeycloakSessionFactory().getProviderFactory(Authenticator.class, CodeLoginAuthenticatorFactory.PROVIDER_ID);
        return factory instanceof CodeLoginAuthenticatorFactory codeLogin ? codeLogin.phone() : new LogPhoneProvider();
    }
}
