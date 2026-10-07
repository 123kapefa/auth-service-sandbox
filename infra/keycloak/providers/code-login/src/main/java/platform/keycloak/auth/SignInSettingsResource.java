package platform.keycloak.auth;

import java.time.Clock;
import java.time.LocalDate;
import java.util.Map;
import java.util.function.Predicate;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.NotAuthorizedException;
import jakarta.ws.rs.PUT;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.PathParam;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;

import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.models.UserModel;
import org.keycloak.services.managers.AppAuthManager;
import org.keycloak.services.managers.AuthenticationManager.AuthResult;
import org.keycloak.services.resource.RealmResourceProvider;

/**
 * /realms/{realm}/platform-sign-in — способы входа проекта для админки (ProjectSignIn).
 * GET methods — состояние способов и 2FA; PUT methods — сохранить всё целиком; GET methods/{способ}/affected —
 * сколько людей входит этим способом и сколько — только им (кого выключение запрёт).
 * Всё — только с токеном учётки, у которой роль realm manage-sign-in (служебная учётка сервиса входа).
 */
public final class SignInSettingsResource implements RealmResourceProvider {

    private final KeycloakSession session;
    private final Clock clock;

    public SignInSettingsResource(KeycloakSession session, Clock clock) {
        this.session = session;
        this.clock = clock;
    }

    @Override
    public Object getResource() {
        return this;
    }

    @Override
    public void close() {
    }

    @GET
    @Path("methods")
    @Produces(MediaType.APPLICATION_JSON)
    public Response methods() {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkManager(realm);
        if (denied != null) {
            return denied;
        }
        return view(realm);
    }

    @PUT
    @Path("methods")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response update(ProjectSignIn.Request request) {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkManager(realm);
        if (denied != null) {
            return denied;
        }
        ProjectSignIn.Settings current = ProjectSignInRealm.settings(session, realm);
        Object parsed = ProjectSignIn.fromRequest(request, current, clock.instant());
        if (parsed instanceof String error) {
            return badRequest(error);
        }
        ProjectSignIn.Settings next = (ProjectSignIn.Settings) parsed;
        String error = ProjectSignIn.validate(next, ProjectSignInRealm.configured(session), today());
        if (error != null) {
            return badRequest(error);
        }
        ProjectSignInRealm.save(session, realm, next);
        return view(realm);
    }

    @GET
    @Path("methods/{method}/affected")
    @Produces(MediaType.APPLICATION_JSON)
    public Response affected(@PathParam("method") String key) {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkManager(realm);
        if (denied != null) {
            return denied;
        }
        ProjectSignIn.Method method = ProjectSignIn.Method.of(key);
        if (method == null) {
            return Response.status(Response.Status.NOT_FOUND).entity(Map.of("error", "method_not_found"))
                .type(MediaType.APPLICATION_JSON).build();
        }
        ProjectSignIn.Settings settings = ProjectSignInRealm.settings(session, realm);
        LocalDate today = today();
        Predicate<ProjectSignIn.Method> on = other -> settings.on(other, today);
        long[] counts = new long[2];
        // Все люди проекта по одному: для большого проекта — медленно, зовётся только из окна выключения.
        session.users().searchForUserStream(realm, Map.of(UserModel.INCLUDE_SERVICE_ACCOUNT, "false"))
            .map(user -> ProjectSignInRealm.user(session, realm, user))
            .forEach(user -> {
                if (ProjectSignIn.ways(user, on.or(method::equals)).contains(method)) {
                    counts[0]++;
                    if (ProjectSignIn.onlyBy(user, method, on)) {
                        counts[1]++;
                    }
                }
            });
        return Response.ok(Map.of("method", method.key(), "users", counts[0], "only", counts[1]))
            .header("Cache-Control", "no-store").build();
    }

    private Response view(RealmModel realm) {
        return Response.ok(ProjectSignIn.view(ProjectSignInRealm.settings(session, realm), ProjectSignInRealm.configured(session), today()))
            .header("Cache-Control", "no-store").build();
    }

    private LocalDate today() {
        return LocalDate.ofInstant(clock.instant(), ProjectSignInRealm.ZONE);
    }

    /** null — можно; иначе ответ с отказом. */
    private Response checkManager(RealmModel realm) {
        AuthResult auth;
        try {
            auth = new AppAuthManager.BearerTokenAuthenticator(session).authenticate();
        } catch (NotAuthorizedException e) {
            auth = null;
        }
        if (auth == null || auth.getUser() == null) {
            return Response.status(Response.Status.UNAUTHORIZED).build();
        }
        RoleModel role = realm.getRole(CodeSettings.MANAGE_ROLE);
        if (role == null || !auth.getUser().hasRole(role)) {
            return Response.status(Response.Status.FORBIDDEN).build();
        }
        return null;
    }

    private static Response badRequest(String error) {
        return Response.status(Response.Status.BAD_REQUEST).entity(Map.of("error", error)).build();
    }
}
