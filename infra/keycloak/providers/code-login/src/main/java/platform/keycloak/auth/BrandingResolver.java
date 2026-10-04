package platform.keycloak.auth;

import java.util.Arrays;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.Map;

import jakarta.ws.rs.core.UriBuilder;

import org.keycloak.models.ClientModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.protocol.oidc.endpoints.AuthorizationEndpoint;
import org.keycloak.services.managers.AuthenticationSessionManager;
import org.keycloak.sessions.AuthenticationSessionModel;

/**
 * Какое оформление показать: проекта (realm) и поверх него — приложения, если вход идёт в приложение.
 * Приложение входа: параметр platform_app запроса входа (его передаёт сервис входа, /auth/login?app=…; Keycloak хранит
 * его в сессии входа), иначе — клиент входа и его platform.app (как у доступа к приложению, AppAccess).
 */
final class BrandingResolver {

    /** Параметр запроса входа: clientId приложения, ради которого входят через общий клиент сервиса входа. */
    static final String APP_PARAM = "platform_app";

    private BrandingResolver() {
    }

    /** Приложение по clientId (null — нет такого клиента). */
    static ClientModel app(RealmModel realm, String clientId) {
        return clientId == null || clientId.isBlank() ? null : realm.getClientByClientId(clientId);
    }

    /** Приложение страницы входа: по сессии входа этой вкладки (cookie и tab_id), иначе по клиенту входа. */
    static ClientModel app(KeycloakSession session, RealmModel realm, String clientId, String tabId) {
        ClientModel client = app(realm, clientId);
        if (client == null) {
            return null;
        }
        if (tabId != null && !tabId.isBlank()) {
            try {
                AuthenticationSessionModel authSession =
                    new AuthenticationSessionManager(session).getCurrentAuthenticationSession(realm, client, tabId);
                ClientModel requested = authSession == null ? null : app(realm, requestedApp(authSession));
                if (requested != null) {
                    return requested;
                }
            } catch (RuntimeException e) {
                // Нет или испорчена cookie сессии входа — оформление по клиенту входа.
            }
        }
        return AppAccess.appOf(client);
    }

    /** Приложение письма из сессии входа (сброс пароля, подтверждение почты); null — письмо не из входа. */
    static ClientModel app(AuthenticationSessionModel authSession) {
        if (authSession == null || authSession.getClient() == null) {
            return null;
        }
        ClientModel requested = app(authSession.getRealm(), requestedApp(authSession));
        return requested != null ? requested : AppAccess.appOf(authSession.getClient());
    }

    private static String requestedApp(AuthenticationSessionModel authSession) {
        return authSession.getClientNote(AuthorizationEndpoint.LOGIN_SESSION_NOTE_ADDITIONAL_REQ_PARAMS_PREFIX + APP_PARAM);
    }

    /** Оформление проекта как есть (без приложения). */
    static Map<String, Object> project(RealmModel realm) {
        Map<String, Object> branding = new LinkedHashMap<>();
        String name = realm.getDisplayName();
        branding.put("name", name == null || name.isBlank() ? realm.getName() : name);
        branding.put("logo", realm.getAttribute(Branding.ATTRIBUTE_LOGO));
        branding.put("color", realm.getAttribute(Branding.ATTRIBUTE_COLOR));
        branding.put("colorDark", realm.getAttribute(Branding.ATTRIBUTE_COLOR_DARK));
        String theme = realm.getAttribute(Branding.ATTRIBUTE_THEME);
        branding.put("theme", theme == null ? Branding.THEME_SYSTEM : theme);
        return branding;
    }

    /** Своё у приложения: null — как у проекта. */
    static Map<String, Object> own(ClientModel app) {
        Map<String, Object> branding = new LinkedHashMap<>();
        branding.put("name", app.getAttribute(Branding.ATTRIBUTE_NAME));
        branding.put("logo", app.getAttribute(Branding.ATTRIBUTE_LOGO));
        branding.put("color", app.getAttribute(Branding.ATTRIBUTE_COLOR));
        branding.put("colorDark", app.getAttribute(Branding.ATTRIBUTE_COLOR_DARK));
        return branding;
    }

    /**
     * Итоговое: проект, поверх — заданное у приложения (название, логотип, акцент). Акцент приложения заменяет акцент
     * проекта парой: свой цвет без цвета для тёмной темы — тот же цвет и в тёмной, не тёмный цвет проекта.
     */
    static Map<String, Object> effective(RealmModel realm, ClientModel app) {
        Map<String, Object> branding = project(realm);
        if (app != null) {
            Map<String, Object> own = own(app);
            if (own.get("color") != null) {
                branding.put("colorDark", null);
            }
            own.forEach((key, value) -> {
                if (value != null) {
                    branding.put(key, value);
                }
            });
        }
        return branding;
    }

    /**
     * Для шаблона писем (branding в html/template.ftl): название, цвет (серый #3c3c3c без своего), текст на цвете и
     * ссылка на логотип — почтовые программы data URL не показывают, а SVG не показывают вовсе.
     */
    static Map<String, Object> forEmail(KeycloakSession session, RealmModel realm, ClientModel app) {
        Map<String, Object> effective = effective(realm, app);
        Map<String, Object> branding = new HashMap<>();
        branding.put("name", effective.get("name"));
        String color = (String) effective.get("color");
        branding.put("color", color == null ? Branding.GRAY : color);
        branding.put("onColor", color == null ? "#f7f7f7" : Branding.textOn(color));
        Branding.Image logo = Branding.decodeLogo((String) effective.get("logo"));
        if (logo != null && !"image/svg+xml".equals(logo.contentType())) {
            try {
                UriBuilder url = session.getContext().getUri().getBaseUriBuilder()
                    .path("realms").path(realm.getName()).path(BrandingResourceFactory.PROVIDER_ID).path("logo")
                    // v — от картинки: заменили логотип — почтовые программы не покажут старый из кэша.
                    .queryParam("v", Integer.toHexString(Arrays.hashCode(logo.bytes())));
                if (app != null) {
                    url.queryParam("app", app.getClientId());
                }
                branding.put("logoUrl", url.build().toString());
            } catch (RuntimeException e) {
                // Письмо не из запроса (по расписанию) — адреса Keycloak не знаем, письмо без логотипа.
            }
        }
        return branding;
    }
}
