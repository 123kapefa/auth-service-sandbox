package platform.keycloak.auth;

import java.util.LinkedHashMap;
import java.util.Map;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.NotAuthorizedException;
import jakarta.ws.rs.PUT;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;

import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.services.managers.AppAuthManager;
import org.keycloak.services.managers.AuthenticationManager.AuthResult;
import org.keycloak.services.resource.RealmResourceProvider;

/**
 * /realms/{realm}/platform-branding — оформление проекта.
 * GET — всем (страница входа читает его при открытии; секретов в нём нет).
 * PUT — только с токеном учётки, у которой роль realm manage-branding (служебная учётка сервиса входа): админка
 * проекта меняет оформление через сервис входа, право manage-realm (весь realm) ему для этого не нужно.
 */
public final class BrandingResource implements RealmResourceProvider {

    private final KeycloakSession session;

    public BrandingResource(KeycloakSession session) {
        this.session = session;
    }

    @Override
    public Object getResource() {
        return this;
    }

    @Override
    public void close() {
    }

    // @Path("") обязателен: без него RESTEasy Reactive (Quarkus) не находит методы корня расширения — 405.
    @GET
    @Path("")
    @Produces(MediaType.APPLICATION_JSON)
    public Response get() {
        // no-cache: сохранили в админке — следующая страница входа уже с новым оформлением.
        return Response.ok(read(session.getContext().getRealm())).header("Cache-Control", "no-cache").build();
    }

    @PUT
    @Path("")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response put(Branding.Representation branding) {
        RealmModel realm = session.getContext().getRealm();
        AuthResult auth;
        try {
            auth = new AppAuthManager.BearerTokenAuthenticator(session).authenticate();
        } catch (NotAuthorizedException e) {
            auth = null;
        }
        if (auth == null || auth.getUser() == null) {
            return Response.status(Response.Status.UNAUTHORIZED).build();
        }
        RoleModel role = realm.getRole(Branding.MANAGE_ROLE);
        if (role == null || !auth.getUser().hasRole(role)) {
            return Response.status(Response.Status.FORBIDDEN).build();
        }

        String error = Branding.validate(branding);
        if (error != null) {
            return Response.status(Response.Status.BAD_REQUEST).entity(Map.of("error", error)).build();
        }

        realm.setDisplayName(branding.name.trim());
        set(realm, Branding.ATTRIBUTE_LOGO, branding.logo);
        set(realm, Branding.ATTRIBUTE_COLOR, branding.color);
        set(realm, Branding.ATTRIBUTE_THEME, branding.theme);
        return Response.ok(read(realm)).build();
    }

    private static Map<String, Object> read(RealmModel realm) {
        Map<String, Object> branding = new LinkedHashMap<>();
        String name = realm.getDisplayName();
        branding.put("name", name == null || name.isBlank() ? realm.getName() : name);
        branding.put("logo", realm.getAttribute(Branding.ATTRIBUTE_LOGO));
        branding.put("color", realm.getAttribute(Branding.ATTRIBUTE_COLOR));
        String theme = realm.getAttribute(Branding.ATTRIBUTE_THEME);
        branding.put("theme", theme == null ? Branding.THEME_SYSTEM : theme);
        return branding;
    }

    private static void set(RealmModel realm, String name, String value) {
        if (value == null) {
            realm.removeAttribute(name);
        } else {
            realm.setAttribute(name, value);
        }
    }
}
