package platform.keycloak.auth;

import java.io.IOException;
import java.text.MessageFormat;
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
import org.keycloak.email.EmailException;
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

/**
 * /realms/{realm}/platform-account-notice/users/{userId} — уведомить человека об изменении в его аккаунте (10.3:
 * способ входа выключен, включён, внешний вход отвязан, админ вернул способы по умолчанию). Письмо в оформлении проекта, если есть почта;
 * иначе SMS (она платная — только когда письмо послать некуда). Вызывает сервис входа токеном служебной учётки —
 * нужна роль manage-users (realm-management), которая у неё и так есть, чтобы менять пользователей.
 * Тело: {"event": "disabled" | "enabled" | "reset", "method": "password" | "phone-code" | "email-code"} или
 * {"event": "unlinked", "method": "yandex" | "esia"}.
 * Ответ: {"sent": "email" | "sms" | "none"}.
 */
public final class AccountNoticeResource implements RealmResourceProvider {

    static final String TEMPLATE = "platform-sign-in-methods.ftl";
    static final List<String> EVENTS = List.of("disabled", "enabled", "unlinked", "reset");

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
        if (notice == null || !EVENTS.contains(notice.event) || !validMethod(notice)) {
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
            Properties messages = AccountNotices.messages(session, realm, user);
            Locale locale = session.getContext().resolveLocale(user);
            String text = text(messages, locale, notice, AccountNotices.realmName(realm));
            String notYou = messages.getProperty("platformSignInMethodsNotYou", "");
            if (AccountNotices.email(session, realm, user, "platformSignInMethodsEmailSubject", TEMPLATE, text, notYou)) {
                return "email";
            }
            String phone = AccountNotices.phone(user);
            if (phone != null) {
                return AccountNotices.phoneProvider(session).message(session, phone, (text + " " + notYou).trim()) ? "sms" : "none";
            }
            return "none";
        } catch (EmailException | IOException e) {
            LOG.warnf(e, "Уведомление о способах входа для %s не отправлено", user.getUsername());
            return "none";
        }
    }

    // У reset способа нет, у unlinked — провайдер (незнакомый — алиасом), у остальных — способ из SignInMethods.ALL.
    static boolean validMethod(Notice notice) {
        return switch (notice.event) {
            case "reset" -> true;
            case "unlinked" -> notice.method != null && !notice.method.isBlank();
            default -> SignInMethods.ALL.contains(notice.method);
        };
    }

    // «В Магазин выключен вход по паролю.» — тексты из темы писем (их же видит письмо и SMS).
    static String text(Properties messages, Locale locale, Notice notice, String realmName) {
        String key = switch (notice.event) {
            case "disabled" -> "platformSignInMethodsDisabledText";
            case "enabled" -> "platformSignInMethodsEnabledText";
            case "unlinked" -> "platformSignInMethodsUnlinkedText";
            default -> "platformSignInMethodsResetText";
        };
        String method = notice.method == null ? "" : messages.getProperty(methodKey(notice.method), notice.method);
        return new MessageFormat(messages.getProperty(key, key), locale).format(new Object[] {realmName, method});
    }

    private static String methodKey(String method) {
        return switch (method) {
            case SignInMethods.PASSWORD -> "platformSignInMethodPassword";
            case SignInMethods.PHONE_CODE -> "platformSignInMethodPhoneCode";
            case SignInMethods.EMAIL_CODE -> "platformSignInMethodEmailCode";
            case "yandex" -> "platformSignInProviderYandex";
            case "esia" -> "platformSignInProviderEsia";
            default -> method;
        };
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
