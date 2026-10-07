package platform.keycloak.auth;

import java.time.Clock;
import java.time.Instant;
import java.time.LocalDate;
import java.time.ZoneId;
import java.util.EnumMap;
import java.util.EnumSet;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;

import org.keycloak.models.IdentityProviderModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RequiredActionProviderModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.WebAuthnPolicy;

/**
 * Способы входа проекта (ProjectSignIn) в Keycloak: настройки — атрибуты realm platform.sign-in.*; ключ доступа,
 * Яндекс ID и Госуслуги дополнительно выключаются переключателями самого Keycloak (ключи доступа realm и регистрация
 * ключа, identity provider) — так их не обойти и мимо наших форм. Сутки переходного периода — по Москве.
 */
final class ProjectSignInRealm {

    static final ZoneId ZONE = ZoneId.of("Europe/Moscow");
    static final Clock CLOCK = Clock.systemUTC();

    private static final String REGISTER_PASSKEY = "webauthn-register-passwordless";

    private ProjectSignInRealm() {
    }

    static LocalDate today() {
        return LocalDate.ofInstant(CLOCK.instant(), ZONE);
    }

    /** Настройки проекта; внешний вход, которого нет в Keycloak или который выключен там, — выключен. */
    static ProjectSignIn.Settings settings(KeycloakSession session, RealmModel realm) {
        ProjectSignIn.Settings stored = ProjectSignIn.read(realm::getAttribute);
        Map<ProjectSignIn.Method, ProjectSignIn.Status> methods = new EnumMap<>(stored.methods());
        for (ProjectSignIn.Method external : ProjectSignIn.EXTERNAL) {
            IdentityProviderModel provider = session.identityProviders().getByAlias(external.key());
            if (provider == null || !provider.isEnabled()) {
                methods.put(external, ProjectSignIn.Status.OFF);
            }
        }
        return new ProjectSignIn.Settings(methods, stored.twoFactorRequired());
    }

    /** Внешний вход, настроенный в Keycloak (с ключами проекта). */
    static Set<ProjectSignIn.Method> configured(KeycloakSession session) {
        return ProjectSignIn.EXTERNAL.stream()
            .filter(external -> session.identityProviders().getByAlias(external.key()) != null)
            .collect(Collectors.toCollection(() -> EnumSet.noneOf(ProjectSignIn.Method.class)));
    }

    /** Сохранить проверенные настройки и включить или выключить то, что выключает сам Keycloak. */
    static void save(KeycloakSession session, RealmModel realm, ProjectSignIn.Settings settings) {
        for (ProjectSignIn.Method method : ProjectSignIn.Method.values()) {
            realm.setAttribute(ProjectSignIn.ATTRIBUTE_PREFIX + method.key(), ProjectSignIn.format(settings.status(method)));
        }
        realm.setAttribute(ProjectSignIn.ATTRIBUTE_PREFIX + ProjectSignIn.TWO_FACTOR, settings.twoFactorRequired() ? "required" : "optional");

        boolean passkeys = settings.status(ProjectSignIn.Method.PASSKEY) instanceof ProjectSignIn.Status.On;
        WebAuthnPolicy policy = realm.getWebAuthnPolicyPasswordless();
        if (!Boolean.valueOf(passkeys).equals(policy.isPasskeysEnabled())) {
            policy.setPasskeysEnabled(passkeys);
            realm.setWebAuthnPolicyPasswordless(policy);
        }
        RequiredActionProviderModel register = realm.getRequiredActionProviderByAlias(REGISTER_PASSKEY);
        if (register != null && register.isEnabled() != passkeys) {
            register.setEnabled(passkeys);
            realm.updateRequiredActionProvider(register);
        }
        for (ProjectSignIn.Method external : ProjectSignIn.EXTERNAL) {
            IdentityProviderModel provider = session.identityProviders().getByAlias(external.key());
            boolean on = settings.status(external) instanceof ProjectSignIn.Status.On;
            if (provider != null && provider.isEnabled() != on) {
                provider.setEnabled(on);
                session.identityProviders().update(provider);
            }
        }
    }

    /** Что есть у человека — для правил проекта. */
    static ProjectSignIn.User user(KeycloakSession session, RealmModel realm, UserModel user) {
        SignInMethods.Facts facts = SignInMethods.facts(session, realm, user);
        Set<ProjectSignIn.Method> linked = session.users().getFederatedIdentitiesStream(realm, user)
            .map(link -> ProjectSignIn.Method.of(link.getIdentityProvider()))
            .filter(method -> method != null && ProjectSignIn.EXTERNAL.contains(method))
            .collect(Collectors.toCollection(() -> EnumSet.noneOf(ProjectSignIn.Method.class)));
        Long created = user.getCreatedTimestamp();
        return new ProjectSignIn.User(user.getUsername().matches("\\d{10,15}"), facts.hasEmail(), facts.hasPassword(),
            facts.hasPasskey(), linked, SignInMethods.effectiveDisabled(facts),
            created == null ? Instant.EPOCH : Instant.ofEpochMilli(created));
    }

    /** Недоступные человеку способы проекта (пусто — всё включено). */
    static Set<ProjectSignIn.Method> blocked(KeycloakSession session, RealmModel realm, UserModel user) {
        ProjectSignIn.Settings settings = settings(session, realm);
        if (settings.allOn()) {
            return Set.of();
        }
        return ProjectSignIn.blocked(settings, user(session, realm, user), today());
    }

    /** Способ проекта, к которому относится способ входа человека (SignInMethods). */
    static ProjectSignIn.Method method(String signInMethod) {
        return switch (signInMethod) {
            case SignInMethods.PASSWORD -> ProjectSignIn.Method.PASSWORD;
            case SignInMethods.PHONE_CODE -> ProjectSignIn.Method.PHONE;
            case SignInMethods.EMAIL_CODE -> ProjectSignIn.Method.EMAIL;
            default -> null;
        };
    }

    /** Логин этого вида (номер, почта) вообще принимается — для незнакомого логина: регистрации нет без «включён». */
    static boolean on(KeycloakSession session, RealmModel realm, ProjectSignIn.Method method) {
        return settings(session, realm).on(method, today());
    }

    static ProjectSignIn.Method method(CodeTarget.Channel channel) {
        return channel == CodeTarget.Channel.SMS ? ProjectSignIn.Method.PHONE : ProjectSignIn.Method.EMAIL;
    }
}
