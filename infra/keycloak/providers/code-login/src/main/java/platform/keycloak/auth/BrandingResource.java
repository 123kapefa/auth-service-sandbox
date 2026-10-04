package platform.keycloak.auth;

import java.util.Map;

import jakarta.ws.rs.Consumes;
import jakarta.ws.rs.GET;
import jakarta.ws.rs.NotAuthorizedException;
import jakarta.ws.rs.PUT;
import jakarta.ws.rs.Path;
import jakarta.ws.rs.PathParam;
import jakarta.ws.rs.Produces;
import jakarta.ws.rs.QueryParam;
import jakarta.ws.rs.core.MediaType;
import jakarta.ws.rs.core.Response;

import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.RoleModel;
import org.keycloak.services.managers.AppAuthManager;
import org.keycloak.services.managers.AuthenticationManager.AuthResult;
import org.keycloak.services.resource.RealmResourceProvider;

/**
 * /realms/{realm}/platform-branding — оформление проекта и приложений.
 * GET — всем (страница входа читает его при открытии; секретов в нём нет): итоговое — проекта и поверх него
 * приложения (?app=clientId или страница входа: ?client_id=…&tab_id=…).
 * GET apps/{clientId} — своё у приложения (null — как у проекта); GET logo — картинка логотипа (для писем).
 * PUT и PUT apps/{clientId} — только с токеном учётки, у которой роль realm manage-branding (служебная учётка сервиса
 * входа): админка проекта меняет оформление через сервис входа, право manage-realm (весь realm) ему для этого не нужно.
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
    public Response get(
        @QueryParam("app") String appId,
        @QueryParam("client_id") String clientId,
        @QueryParam("tab_id") String tabId) {
        RealmModel realm = session.getContext().getRealm();
        ClientModel app = appId != null
            ? BrandingResolver.app(realm, appId)
            : BrandingResolver.app(session, realm, clientId, tabId);
        // no-cache: сохранили в админке — следующая страница входа уже с новым оформлением.
        return Response.ok(BrandingResolver.effective(realm, app)).header("Cache-Control", "no-cache").build();
    }

    @PUT
    @Path("")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response put(Branding.Representation branding) {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkManager(realm);
        if (denied != null) {
            return denied;
        }

        String error = Branding.validate(branding);
        if (error != null) {
            return badRequest(error);
        }

        realm.setDisplayName(branding.name.trim());
        realm.setAttribute(Branding.ATTRIBUTE_THEME, branding.theme);
        setRealm(realm, Branding.ATTRIBUTE_LOGO, branding.logo);
        setRealm(realm, Branding.ATTRIBUTE_COLOR, branding.color);
        return Response.ok(BrandingResolver.project(realm)).build();
    }

    @GET
    @Path("apps/{clientId}")
    @Produces(MediaType.APPLICATION_JSON)
    public Response getApp(@PathParam("clientId") String clientId) {
        ClientModel app = BrandingResolver.app(session.getContext().getRealm(), clientId);
        if (app == null) {
            return notFoundApp();
        }
        return Response.ok(BrandingResolver.own(app)).header("Cache-Control", "no-cache").build();
    }

    @PUT
    @Path("apps/{clientId}")
    @Consumes(MediaType.APPLICATION_JSON)
    @Produces(MediaType.APPLICATION_JSON)
    public Response putApp(@PathParam("clientId") String clientId, Branding.AppRepresentation branding) {
        RealmModel realm = session.getContext().getRealm();
        Response denied = checkManager(realm);
        if (denied != null) {
            return denied;
        }

        ClientModel app = BrandingResolver.app(realm, clientId);
        if (app == null) {
            return notFoundApp();
        }

        String error = Branding.validate(branding);
        if (error != null) {
            return badRequest(error);
        }

        setClient(app, Branding.ATTRIBUTE_NAME, branding.name == null ? null : branding.name.trim());
        setClient(app, Branding.ATTRIBUTE_LOGO, branding.logo);
        setClient(app, Branding.ATTRIBUTE_COLOR, branding.color);
        return Response.ok(BrandingResolver.own(app)).build();
    }

    /**
     * Логотип картинкой — для писем: почтовые программы не показывают data URL. SVG с этого адреса открыт в браузере
     * не должен выполнить ничего: CSP sandbox, без скриптов, и nosniff.
     */
    @GET
    @Path("logo")
    public Response logo(@QueryParam("app") String appId) {
        RealmModel realm = session.getContext().getRealm();
        Object logo = BrandingResolver.effective(realm, BrandingResolver.app(realm, appId)).get("logo");
        Branding.Image image = Branding.decodeLogo((String) logo);
        if (image == null) {
            return Response.status(Response.Status.NOT_FOUND).build();
        }
        return Response.ok(image.bytes(), image.contentType())
            .header("Cache-Control", "public, max-age=300")
            .header("Content-Security-Policy", "default-src 'none'; style-src 'unsafe-inline'; sandbox")
            .header("X-Content-Type-Options", "nosniff")
            .build();
    }

    /** null — можно менять оформление; иначе ответ с отказом. */
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
        RoleModel role = realm.getRole(Branding.MANAGE_ROLE);
        if (role == null || !auth.getUser().hasRole(role)) {
            return Response.status(Response.Status.FORBIDDEN).build();
        }
        return null;
    }

    private static Response badRequest(String error) {
        return Response.status(Response.Status.BAD_REQUEST).entity(Map.of("error", error)).build();
    }

    // Тело с error отличает «нет приложения» от «нет плагина» (404 без тела).
    private static Response notFoundApp() {
        return Response.status(Response.Status.NOT_FOUND).entity(Map.of("error", "app_not_found"))
            .type(MediaType.APPLICATION_JSON).build();
    }

    private static void setRealm(RealmModel realm, String name, String value) {
        if (value == null) {
            realm.removeAttribute(name);
        } else {
            realm.setAttribute(name, value);
        }
    }

    private static void setClient(ClientModel client, String name, String value) {
        if (value == null) {
            client.removeAttribute(name);
        } else {
            client.setAttribute(name, value);
        }
    }
}
