package platform.keycloak.auth;

import java.time.Clock;
import java.time.LocalDate;
import java.util.Map;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.NotAuthorizedException;
import jakarta.ws.rs.PUT;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;

import org.keycloak.authentication.Authenticator;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.services.managers.AppAuthManager;
import org.keycloak.services.managers.AuthenticationManager.AuthResult;
import org.keycloak.services.resource.RealmResourceProvider;

/**
 * /realms/{realm}/platform-codes — коды проекта для админки («Вход и коды», CodeSettings).
 * GET settings — настройки, по которым коды отправляются сейчас; PUT settings — поменять (пишет атрибуты realm
 * platform.codes.*, плагин применяет их со следующей отправки); GET usage — расход за сегодня.
 * Всё — только с токеном учётки, у которой роль realm manage-sign-in (служебная учётка сервиса входа).
 */
public final class CodeSettingsResource implements RealmResourceProvider {

    private final KeycloakSession session;
    private final Clock clock;

    public CodeSettingsResource(KeycloakSession session, Clock clock) {
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
    @Path("settings")
    @Produces(MediaType.APPLICATION_JSON)
    public Response settings() {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkManager(realm);
        if (denied != null) {
            return denied;
        }
        return noCache(CodeSettings.view(codeLogin().policy(realm)));
    }

    @PUT
    @Path("settings")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response update(CodeSettings.Representation settings) {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkManager(realm);
        if (denied != null) {
            return denied;
        }
        String error = CodeSettings.validate(settings);
        if (error != null) {
            return Response.status(Response.Status.BAD_REQUEST).entity(Map.of("error", error)).build();
        }
        CodeSettings.attributes(settings).forEach((key, value) ->
            realm.setAttribute(CodeLimitPolicy.REALM_ATTRIBUTE_PREFIX + key, value));
        return noCache(CodeSettings.view(codeLogin().policy(realm)));
    }

    @GET
    @Path("usage")
    @Produces(MediaType.APPLICATION_JSON)
    public Response usage() {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkManager(realm);
        if (denied != null) {
            return denied;
        }
        CodeLimitPolicy policy = codeLogin().policy(realm);
        LocalDate today = LocalDate.ofInstant(clock.instant(), policy.zone());
        return noCache(CodeSettings.usage(new CodeLimitStore(session, realm).day(today), policy, today));
    }

    private CodeLoginAuthenticatorFactory codeLogin() {
        return (CodeLoginAuthenticatorFactory) session.getKeycloakSessionFactory()
            .getProviderFactory(Authenticator.class, CodeLoginAuthenticatorFactory.PROVIDER_ID);
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

    private static Response noCache(Object body) {
        return Response.ok(body).header("Cache-Control", "no-store").build();
    }
}
