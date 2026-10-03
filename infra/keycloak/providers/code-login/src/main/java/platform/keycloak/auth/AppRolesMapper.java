package platform.keycloak.auth;

import java.util.ArrayList;
import java.util.List;

import org.keycloak.models.ClientModel;
import org.keycloak.models.ClientSessionContext;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.ProtocolMapperModel;
import org.keycloak.models.UserModel;
import org.keycloak.models.UserSessionModel;
import org.keycloak.protocol.oidc.mappers.AbstractOIDCProtocolMapper;
import org.keycloak.protocol.oidc.mappers.OIDCAccessTokenMapper;
import org.keycloak.protocol.oidc.mappers.OIDCAttributeMapperHelper;
import org.keycloak.protocol.oidc.mappers.TokenIntrospectionTokenMapper;
import org.keycloak.protocol.oidc.mappers.UserInfoTokenMapper;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.representations.IDToken;

/**
 * Роли и права приложения в токене. role и available_roles — только роли (режимы) клиента приложения, без прав
 * (стандартный маппер ролей клиента положил бы и права: роли составные). permissions — права активного режима:
 * админ в режиме покупателя получает права покупателя. Сервисы читают права из токена — карта в их коде не нужна.
 * access_version и mode_version — версии доступа пользователя и роли активного режима: по ним сервисы узнают, что
 * доступ изменился, и просят клиента обновить токен (умный рефреш).
 * Токен служебной учётки (сервис проекта по ключу) — без ролей пользователя (у сервиса нет режимов, роли по умолчанию
 * realm ему не нужны), permissions — выданные сервису права, client_id — какой сервис: по нему сервисы отличают
 * сервис от человека.
 */
public final class AppRolesMapper extends AbstractOIDCProtocolMapper
    implements OIDCAccessTokenMapper, UserInfoTokenMapper, TokenIntrospectionTokenMapper {

    public static final String PROVIDER_ID = "platform-app-roles";
    public static final String ROLES_CLIENT = "roles.client";
    public static final String MODE_ATTRIBUTE = "mode.attribute";
    private static final String DEFAULT_MODE_ATTRIBUTE = "active_mode";
    static final String ACCESS_VERSION = "access_version";
    static final String MODE_VERSION = "mode_version";
    static final String SERVICE_CLIENT = "client_id";

    private static final List<ProviderConfigProperty> CONFIG = new ArrayList<>();

    static {
        ProviderConfigProperty client = new ProviderConfigProperty();
        client.setName(ROLES_CLIENT);
        client.setLabel("Клиент приложения");
        client.setHelpText("Клиент, роли которого — роли и права приложения (например, auth-api).");
        client.setType(ProviderConfigProperty.CLIENT_LIST_TYPE);
        CONFIG.add(client);
        ProviderConfigProperty mode = new ProviderConfigProperty();
        mode.setName(MODE_ATTRIBUTE);
        mode.setLabel("Атрибут активного режима");
        mode.setHelpText("Атрибут пользователя с активным режимом в этом приложении (по умолчанию active_mode).");
        mode.setType(ProviderConfigProperty.STRING_TYPE);
        mode.setDefaultValue(DEFAULT_MODE_ATTRIBUTE);
        CONFIG.add(mode);
        OIDCAttributeMapperHelper.addIncludeInTokensConfig(CONFIG, AppRolesMapper.class);
    }

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Sandbox: роли приложения";
    }

    @Override
    public String getDisplayCategory() {
        return TOKEN_MAPPER_CATEGORY;
    }

    @Override
    public String getHelpText() {
        return "role и available_roles — роли (режимы) приложения без прав; permissions — права активного режима.";
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return CONFIG;
    }

    @Override
    protected void setClaim(IDToken token, ProtocolMapperModel mapping, UserSessionModel userSession,
        KeycloakSession session, ClientSessionContext clientSession) {
        ClientModel client = session.getContext().getRealm().getClientByClientId(mapping.getConfig().get(ROLES_CLIENT));
        if (client == null) {
            return;
        }

        UserModel user = userSession.getUser();
        if (user.getServiceAccountClientLink() != null) {
            ClientModel service = session.getContext().getRealm().getClientById(user.getServiceAccountClientLink());
            token.getOtherClaims().put("role", List.of());
            token.getOtherClaims().put("available_roles", List.of());
            token.getOtherClaims().put("permissions", AppRoles.servicePermissionsOf(client, user));
            token.getOtherClaims().put(SERVICE_CLIENT, service == null ? user.getServiceAccountClientLink() : service.getClientId());
            return;
        }

        List<String> roles = AppRoles.rolesOf(client, user);
        String modeAttribute = mapping.getConfig().getOrDefault(MODE_ATTRIBUTE, DEFAULT_MODE_ATTRIBUTE);
        token.getOtherClaims().put("role", roles);
        token.getOtherClaims().put("available_roles", roles);
        String activeMode = user.getFirstAttribute(modeAttribute);
        token.getOtherClaims().put("permissions", AppRoles.permissionsOf(client, user, activeMode));
        putVersion(token, ACCESS_VERSION, user.getFirstAttribute(ACCESS_VERSION));
        putVersion(token, MODE_VERSION, AppRoles.versionOf(client, user, activeMode));
    }

    // Версия — число (миллисекунды); нет или не число — claim не кладётся (сервис считает её нулём).
    private static void putVersion(IDToken token, String claim, String value) {
        if (value == null) {
            return;
        }

        try {
            token.getOtherClaims().put(claim, Long.parseLong(value));
        } catch (NumberFormatException ignored) {
            // чужое значение атрибута — без версии
        }
    }
}
