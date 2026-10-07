package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Instant;
import java.time.LocalDate;
import java.util.EnumMap;
import java.util.EnumSet;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;

import org.junit.jupiter.api.Test;

import platform.keycloak.auth.ProjectSignIn.Method;
import platform.keycloak.auth.ProjectSignIn.Settings;
import platform.keycloak.auth.ProjectSignIn.Status;

class ProjectSignInTest {

    private static final LocalDate TODAY = LocalDate.of(2026, 10, 7);
    private static final Instant SINCE = Instant.parse("2026-10-07T09:00:00Z");
    private static final Instant BEFORE = SINCE.minusSeconds(86_400);
    private static final Instant AFTER = SINCE.plusSeconds(60);
    private static final Set<Method> BOTH_EXTERNAL = EnumSet.of(Method.YANDEX, Method.ESIA);

    private static ProjectSignIn.User phoneOnly(Instant created) {
        return new ProjectSignIn.User(true, false, false, false, Set.of(), Set.of(), created);
    }

    private static ProjectSignIn.User phoneAndEmail() {
        return new ProjectSignIn.User(true, true, true, false, Set.of(), Set.of(), BEFORE);
    }

    private static Settings with(Method method, Status status) {
        Map<Method, Status> methods = new EnumMap<>(Method.class);
        methods.put(method, status);
        return new Settings(methods, false);
    }

    @Test
    void attributes_roundTrip_unknownIsOn() {
        Status transition = new Status.Transition(LocalDate.of(2026, 11, 5), SINCE);
        assertEquals(transition, ProjectSignIn.parse(ProjectSignIn.format(transition)));
        assertEquals(Status.OFF, ProjectSignIn.parse("off"));
        assertEquals(Status.ON, ProjectSignIn.parse(null));
        assertEquals(Status.ON, ProjectSignIn.parse("transition:bad:1"));
        assertTrue(ProjectSignIn.read(key -> null).allOn());
    }

    @Test
    void transition_endsAfterDate() {
        Settings settings = with(Method.PHONE, new Status.Transition(TODAY, SINCE));
        assertInstanceOf(Status.Transition.class, settings.effective(Method.PHONE, TODAY));
        assertEquals(Status.OFF, settings.effective(Method.PHONE, TODAY.plusDays(1)));
    }

    @Test
    void passwordNeedsLogin_phoneOffKillsPasswordWithPhone() {
        ProjectSignIn.User user = new ProjectSignIn.User(true, false, true, false, Set.of(), Set.of(), BEFORE);
        assertEquals(EnumSet.of(Method.PHONE, Method.PASSWORD), ProjectSignIn.ways(user, method -> true));
        assertEquals(Set.of(), ProjectSignIn.ways(user, method -> method != Method.PHONE));
        assertTrue(ProjectSignIn.onlyBy(user, Method.PHONE, method -> true));
        assertFalse(ProjectSignIn.onlyBy(user, Method.PASSWORD, method -> true));
    }

    @Test
    void userDisabledMethods_notCounted() {
        ProjectSignIn.User user = new ProjectSignIn.User(true, true, false, false, Set.of(), Set.of(SignInMethods.EMAIL_CODE), BEFORE);
        assertTrue(ProjectSignIn.onlyBy(user, Method.PHONE, method -> true));
    }

    @Test
    void off_blocksEveryone() {
        Settings settings = with(Method.PASSWORD, Status.OFF);
        assertEquals(EnumSet.of(Method.PASSWORD), ProjectSignIn.blocked(settings, phoneAndEmail(), TODAY));
    }

    @Test
    void transition_keptOnlyForOldOnlyThisWay() {
        Settings settings = with(Method.PHONE, new Status.Transition(LocalDate.of(2026, 11, 5), SINCE));
        assertEquals(Set.of(), ProjectSignIn.blocked(settings, phoneOnly(BEFORE), TODAY));
        assertEquals(EnumSet.of(Method.PHONE), ProjectSignIn.transitional(settings, phoneOnly(BEFORE), TODAY));
        // пришёл после начала периода
        assertEquals(EnumSet.of(Method.PHONE), ProjectSignIn.blocked(settings, phoneOnly(AFTER), TODAY));
        // есть почта — входит ей
        assertEquals(EnumSet.of(Method.PHONE), ProjectSignIn.blocked(settings, phoneAndEmail(), TODAY));
        assertEquals(Set.of(), ProjectSignIn.transitional(settings, phoneAndEmail(), TODAY));
        // период кончился
        assertEquals(EnumSet.of(Method.PHONE), ProjectSignIn.blocked(settings, phoneOnly(BEFORE), LocalDate.of(2026, 11, 6)));
    }

    @Test
    void validate_keepsRegistration() {
        Map<Method, Status> methods = new EnumMap<>(Method.class);
        methods.put(Method.PHONE, Status.OFF);
        methods.put(Method.EMAIL, new Status.Transition(TODAY.plusDays(10), SINCE));
        methods.put(Method.YANDEX, Status.OFF);
        methods.put(Method.ESIA, Status.OFF);
        assertNotNull(ProjectSignIn.validate(new Settings(methods, false), BOTH_EXTERNAL, TODAY));
        methods.put(Method.ESIA, Status.ON);
        assertNull(ProjectSignIn.validate(new Settings(methods, false), BOTH_EXTERNAL, TODAY));
    }

    @Test
    void validate_externalNeedsKeys_transitionRules() {
        assertNotNull(ProjectSignIn.validate(Settings.ALL_ON, Set.of(), TODAY), "Яндекс ID без ключей не включить");
        Map<Method, Status> methods = new EnumMap<>(Method.class);
        methods.put(Method.YANDEX, Status.OFF);
        methods.put(Method.ESIA, Status.OFF);
        assertNull(ProjectSignIn.validate(new Settings(methods, false), Set.of(), TODAY));

        methods.put(Method.PASSKEY, new Status.Transition(TODAY.plusDays(10), SINCE));
        assertNotNull(ProjectSignIn.validate(new Settings(methods, false), Set.of(), TODAY), "ключ — только сразу");
        methods.put(Method.PASSKEY, Status.ON);
        methods.put(Method.PASSWORD, new Status.Transition(TODAY, SINCE));
        assertNotNull(ProjectSignIn.validate(new Settings(methods, false), Set.of(), TODAY), "дата — в будущем");
        methods.put(Method.PASSWORD, new Status.Transition(TODAY.plusDays(400), SINCE));
        assertNotNull(ProjectSignIn.validate(new Settings(methods, false), Set.of(), TODAY), "не дальше года");
    }

    @Test
    void request_keepsTransitionStart() {
        ProjectSignIn.Request request = new ProjectSignIn.Request();
        request.methods = new HashMap<>();
        for (Method method : Method.values()) {
            ProjectSignIn.MethodRequest value = new ProjectSignIn.MethodRequest();
            value.status = "on";
            request.methods.put(method.key(), value);
        }
        request.methods.get("phone").status = "transition";
        request.methods.get("phone").until = "2026-11-05";
        request.twoFactor = "required";

        Settings fresh = (Settings) ProjectSignIn.fromRequest(request, Settings.ALL_ON, AFTER);
        assertEquals(new Status.Transition(LocalDate.of(2026, 11, 5), AFTER), fresh.status(Method.PHONE));
        assertTrue(fresh.twoFactorRequired());

        Settings extended = (Settings) ProjectSignIn.fromRequest(request,
            with(Method.PHONE, new Status.Transition(LocalDate.of(2026, 10, 20), SINCE)), AFTER);
        assertEquals(SINCE, ((Status.Transition) extended.status(Method.PHONE)).since());

        request.methods.get("phone").until = "5 ноября";
        assertInstanceOf(String.class, ProjectSignIn.fromRequest(request, Settings.ALL_ON, AFTER));
        request.methods.remove("esia");
        assertInstanceOf(String.class, ProjectSignIn.fromRequest(request, Settings.ALL_ON, AFTER));
    }

    @Test
    void view_showsDatesAndConfigured() {
        Map<String, Object> view = ProjectSignIn.view(with(Method.PHONE, new Status.Transition(LocalDate.of(2026, 11, 5), SINCE)),
            EnumSet.of(Method.YANDEX), TODAY);
        @SuppressWarnings("unchecked")
        Map<String, Map<String, Object>> methods = (Map<String, Map<String, Object>>) view.get("methods");
        assertEquals("transition", methods.get("phone").get("status"));
        assertEquals("2026-11-05", methods.get("phone").get("until"));
        assertEquals(false, methods.get("esia").get("configured"));
        assertEquals("optional", view.get("twoFactor"));
    }

    @Test
    void loginSteps_passwordOffInProject_unknownGetsCode() {
        assertEquals(new LoginSteps.View(false, List.of(LoginSteps.PASSKEY, SignInMethods.PHONE_CODE)),
            LoginSteps.view(Optional.of(CodeTarget.Channel.SMS), null, true, false));
        assertEquals(Optional.of(SignInMethods.EMAIL_CODE),
            LoginSteps.view(Optional.of(CodeTarget.Channel.EMAIL), null, false, false).autoStart());
    }
}
