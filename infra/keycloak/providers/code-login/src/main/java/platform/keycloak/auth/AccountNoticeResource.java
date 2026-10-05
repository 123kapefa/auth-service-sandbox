package platform.keycloak.auth;

import java.io.IOException;
import java.text.MessageFormat;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Properties;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.NotAuthorizedException;
import jakarta.ws.rs.POST;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.PathParam;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;

import org.jboss.logging.Logger;
import org.keycloak.authentication.Authenticator;
import org.keycloak.email.EmailException;
import org.keycloak.email.EmailTemplateProvider;
import org.keycloak.models.AdminRoles;
import org.keycloak.models.ClientModel;
import org.keycloak.models.Constants;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;
import org.keycloak.services.managers.AppAuthManager;
import org.keycloak.services.managers.AuthenticationManager.AuthResult;
import org.keycloak.services.resource.RealmResourceProvider;
import org.keycloak.theme.Theme;

/**
 * /realms/{realm}/platform-account-notice/users/{userId} — уведомить человека об изменении в его аккаунте (10.3:
 * способ входа выключен, включён, админ вернул способы по умолчанию). Письмо в оформлении проекта, если есть почта;
 * иначе SMS (она платная — только когда письмо послать некуда). Вызывает сервис входа токеном служебной учётки —
 * нужна роль manage-users (realm-management), которая у неё и так есть, чтобы менять пользователей.
 * Тело: {"event": "disabled" | "enabled" | "reset", "method": "password" | "phone-code" | "email-code"}.
 * Ответ: {"sent": "email" | "sms" | "none"}.
 */
public final class AccountNoticeResource implements RealmResourceProvider {

    static final String TEMPLATE = "platform-sign-in-methods.ftl";
    static final List<String> EVENTS = List.of("disabled", "enabled", "reset");

    private static final Logger LOG = Logger.getLogger(AccountNoticeResource.class);

    private final KeycloakSession session;

    public AccountNoticeResource(KeycloakSession session) {
        this.session = session;
    }

    /** Тело запроса. */
    public static final class Notice {
        public String event;
        public String method;
    }

    @Override
    public Object getResource() {
        return this;
    }

    @Override
    public void close() {
    }

    @POST
    @Path("users/{userId}")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response notify(@PathParam("userId") String userId, Notice notice) {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkCaller(realm);
        if (denied != null) {
            return denied;
        }
        if (notice == null || !EVENTS.contains(notice.event)
            || (!"reset".equals(notice.event) && !SignInMethods.ALL.contains(notice.method))) {
            return Response.status(Response.Status.BAD_REQUEST).entity(Map.of("error", "invalid_notice"))
                .type(MediaType.APPLICATION_JSON).build();
        }
        UserModel user = session.users().getUserById(realm, userId);
        if (user == null) {
            return Response.status(Response.Status.NOT_FOUND).entity(Map.of("error", "user_not_found"))
                .type(MediaType.APPLICATION_JSON).build();
        }

        String sent = send(realm, user, notice);
        return Response.ok(Map.of("sent", sent)).build();
    }

    private String send(RealmModel realm, UserModel user, Notice notice) {
        try {
            Locale locale = session.getContext().resolveLocale(user);
            Properties messages = session.theme().getTheme(Theme.Type.EMAIL).getEnhancedMessages(realm, locale);
            String realmName = (String) BrandingResolver.effective(realm, null).get("name");
            String text = text(messages, locale, notice, realmName);

            if (user.getEmail() != null && !user.getEmail().isBlank()) {
                Map<String, Object> attributes = new HashMap<>();
                attributes.put("noticeText", text);
                attributes.put("noticeNotYou", messages.getProperty("platformSignInMethodsNotYou", ""));
                session.getProvider(EmailTemplateProvider.class).setRealm(realm).setUser(user)
                    .send("platformSignInMethodsEmailSubject", List.of(), TEMPLATE, attributes);
                return "email";
            }

            String phone = phone(user);
            if (phone != null) {
                String sms = text + " " + messages.getProperty("platformSignInMethodsNotYou", "");
                return phoneProvider().message(session, phone, sms.trim()) ? "sms" : "none";
            }
            return "none";
        } catch (EmailException | IOException e) {
            LOG.warnf(e, "Уведомление о способах входа для %s не отправлено", user.getUsername());
            return "none";
        }
    }

    // «В Магазин выключен вход по паролю.» — тексты из темы писем (их же видит письмо и SMS).
    static String text(Properties messages, Locale locale, Notice notice, String realmName) {
        String key = switch (notice.event) {
            case "disabled" -> "platformSignInMethodsDisabledText";
            case "enabled" -> "platformSignInMethodsEnabledText";
            default -> "platformSignInMethodsResetText";
        };
        String method = notice.method == null ? "" : messages.getProperty(methodKey(notice.method), notice.method);
        return new MessageFormat(messages.getProperty(key, key), locale).format(new Object[] {realmName, method});
    }

    private static String methodKey(String method) {
        return switch (method) {
            case SignInMethods.PASSWORD -> "platformSignInMethodPassword";
            case SignInMethods.PHONE_CODE -> "platformSignInMethodPhoneCode";
            default -> "platformSignInMethodEmailCode";
        };
    }

    // Номер для SMS: атрибут phone («+79990000001») или логин-номер.
    private static String phone(UserModel user) {
        String phone = user.getFirstAttribute("phone");
        if (phone != null && !phone.isBlank()) {
            return PhoneNormalizer.normalizeLogin(phone);
        }
        return user.getUsername().matches("\\d{10,15}") ? user.getUsername() : null;
    }

    private PhoneProvider phoneProvider() {
        Object factory = session.getKeycloakSessionFactory().getProviderFactory(Authenticator.class, CodeLoginAuthenticatorFactory.PROVIDER_ID);
        return factory instanceof CodeLoginAuthenticatorFactory codeLogin ? codeLogin.phone() : new LogPhoneProvider();
    }

    private Response checkCaller(RealmModel realm) {
        AuthResult auth;
        try {
            auth = new AppAuthManager.BearerTokenAuthenticator(session).authenticate();
        } catch (NotAuthorizedException e) {
            auth = null;
        }
        if (auth == null || auth.getUser() == null) {
            return Response.status(Response.Status.UNAUTHORIZED).build();
        }
        ClientModel management = realm.getClientByClientId(Constants.REALM_MANAGEMENT_CLIENT_ID);
        RoleModel role = management == null ? null : management.getRole(AdminRoles.MANAGE_USERS);
        if (role == null || !auth.getUser().hasRole(role)) {
            return Response.status(Response.Status.FORBIDDEN).build();
        }
        return null;
    }
}
