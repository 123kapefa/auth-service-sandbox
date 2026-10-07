package platform.keycloak.auth;

import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.function.Supplier;
import java.util.stream.Collectors;

import jakarta.ws.rs.core.Response;

import org.keycloak.authentication.AuthenticationFlowContext;
import org.keycloak.authentication.AuthenticationFlowError;
import org.keycloak.events.Errors;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;

/**
 * Способы входа, которые человек выключил у себя (10.3): пароль, код на телефон, код на почту. Выключенный способ не
 * пускает никого — даже с верным паролем или кодом. Список хранится в атрибуте пользователя disabled_sign_in_methods
 * (через запятую); пишет его только сервис входа (по правилам, API кабинета), сам человек через Keycloak — не может
 * (атрибут вне профиля, unmanagedAttributePolicy ADMIN_EDIT).
 *
 * Сервис не даёт запереть себя, а здесь — страховка на случай, если опору убрали потом (удалили ключ доступа или
 * приложение-аутентификатор на странице Keycloak). Те же правила — в сервисе (SignInMethodRules), держать одинаковыми:
 * <ul>
 *   <li>пароль и код на телефон выключены, только пока есть ключ доступа или приложение-аутентификатор;</li>
 *   <li>если не осталось ни одного способа (нет ключа доступа и внешнего входа, а всё доступное выключено) —
 *       выключенного нет совсем.</li>
 * </ul>
 */
final class SignInMethods {

    static final String ATTRIBUTE = "disabled_sign_in_methods";

    static final String PASSWORD = "password";
    static final String PHONE_CODE = "phone-code";
    static final String EMAIL_CODE = "email-code";
    static final List<String> ALL = List.of(PASSWORD, PHONE_CODE, EMAIL_CODE);

    /** Деталь события Keycloak: почему вход не пустили. */
    static final String EVENT_REASON = "reason";

    private static final String PASSKEY = "webauthn-passwordless";
    private static final String OTP = "otp";

    private SignInMethods() {
    }

    /**
     * Что есть у человека.
     *
     * @param hasPassword      задан пароль
     * @param hasPhone         есть телефон (код на телефон)
     * @param hasEmail         есть почта (код на почту)
     * @param hasPasskey       есть ключ доступа
     * @param hasAuthenticator есть приложение-аутентификатор (2FA)
     * @param hasExternal      привязан внешний вход (Яндекс ID, Госуслуги)
     * @param disabled         выключенные в атрибуте способы
     */
    record Facts(boolean hasPassword, boolean hasPhone, boolean hasEmail, boolean hasPasskey, boolean hasAuthenticator,
                 boolean hasExternal, Set<String> disabled) {
    }

    /** Способы из атрибута; незнакомые значения пропускаются. */
    static Set<String> parse(String value) {
        if (value == null || value.isBlank()) {
            return Set.of();
        }
        return Arrays.stream(value.split(","))
            .map(String::trim)
            .filter(ALL::contains)
            .collect(Collectors.toCollection(LinkedHashSet::new));
    }

    /** Есть ли у человека этот способ вообще (пароль задан, телефон или почта указаны). */
    static boolean available(Facts facts, String method) {
        return switch (method) {
            case PASSWORD -> facts.hasPassword();
            case PHONE_CODE -> facts.hasPhone();
            case EMAIL_CODE -> facts.hasEmail();
            default -> false;
        };
    }

    /** Какие способы выключены на самом деле — с учётом страховки (см. описание класса). */
    static Set<String> effectiveDisabled(Facts facts) {
        boolean anchored = facts.hasPasskey() || facts.hasAuthenticator();
        Set<String> result = new LinkedHashSet<>();
        for (String method : ALL) {
            if (facts.disabled().contains(method) && (EMAIL_CODE.equals(method) || anchored)) {
                result.add(method);
            }
        }
        boolean nothingLeft = !facts.hasPasskey() && !facts.hasExternal()
            && ALL.stream().filter(method -> available(facts, method)).allMatch(result::contains);
        return nothingLeft ? Set.of() : result;
    }

    /** Выключен ли способ у пользователя — им самим или в проекте (ProjectSignIn). */
    static boolean isDisabled(KeycloakSession session, RealmModel realm, UserModel user, String method) {
        return disabledByUser(session, realm, user, method) || offInProject(session, realm, user, method);
    }

    /** Выключил ли способ сам человек. Пустой атрибут — сразу нет, без чтения учётных данных. */
    static boolean disabledByUser(KeycloakSession session, RealmModel realm, UserModel user, String method) {
        Set<String> disabled = parse(user.getFirstAttribute(ATTRIBUTE));
        if (!disabled.contains(method)) {
            return false;
        }
        return effectiveDisabled(facts(session, realm, user, disabled)).contains(method);
    }

    /** Способ выключен в проекте — для всех или у человека нет права переходного периода. */
    static boolean offInProject(KeycloakSession session, RealmModel realm, UserModel user, String method) {
        ProjectSignIn.Method project = ProjectSignInRealm.method(method);
        return project != null && ProjectSignInRealm.blocked(session, realm, user).contains(project);
    }

    /**
     * Пароль верный, но вход по паролю выключен: отказ — тот же, что на неверный пароль (по ответу не понять,
     * подошёл ли пароль). В журнале событий — причина password_disabled. true — отказали.
     */
    static boolean rejectPassword(AuthenticationFlowContext context, UserModel user, Supplier<Response> challenge,
                                  boolean clearUser) {
        if (!disabledByUser(context.getSession(), context.getRealm(), user, PASSWORD)) {
            return false;
        }
        context.getEvent().user(user).detail(EVENT_REASON, "password_disabled");
        context.getEvent().error(Errors.INVALID_USER_CREDENTIALS);
        context.failureChallenge(AuthenticationFlowError.INVALID_CREDENTIALS, challenge.get());
        if (clearUser) {
            context.clearUser();
        }
        return true;
    }

    /**
     * Вход по паролю выключен в проекте (ProjectSignIn): отказ с понятным сообщением platformPasswordOff — до проверки
     * пароля, чтобы по ответу не понять, подошёл ли он. true — отказали.
     */
    static boolean rejectPasswordOff(AuthenticationFlowContext context, UserModel user, Supplier<Response> challenge,
                                     boolean clearUser) {
        if (!offInProject(context.getSession(), context.getRealm(), user, PASSWORD)) {
            return false;
        }
        context.getEvent().user(user).detail(EVENT_REASON, "password_off_in_project");
        context.getEvent().error(Errors.INVALID_USER_CREDENTIALS);
        context.failureChallenge(AuthenticationFlowError.INVALID_CREDENTIALS, challenge.get());
        if (clearUser) {
            context.clearUser();
        }
        return true;
    }

    /** Что есть у пользователя, с выключенными способами из атрибута (до страховки). */
    static Facts facts(KeycloakSession session, RealmModel realm, UserModel user) {
        return facts(session, realm, user, parse(user.getFirstAttribute(ATTRIBUTE)));
    }

    private static Facts facts(KeycloakSession session, RealmModel realm, UserModel user, Set<String> disabled) {
        return new Facts(
            has(user, PASSWORD),
            hasPhone(user),
            user.getEmail() != null && !user.getEmail().isBlank(),
            has(user, PASSKEY),
            has(user, OTP),
            session.users().getFederatedIdentitiesStream(realm, user).findAny().isPresent(),
            disabled);
    }

    private static boolean has(UserModel user, String type) {
        return user.credentialManager().getStoredCredentialsByTypeStream(type).findAny().isPresent();
    }

    // Телефон — атрибут phone; у входа по номеру логин — сам номер цифрами.
    private static boolean hasPhone(UserModel user) {
        String phone = user.getFirstAttribute("phone");
        return (phone != null && !phone.isBlank()) || user.getUsername().matches("\\d{10,15}");
    }
}
