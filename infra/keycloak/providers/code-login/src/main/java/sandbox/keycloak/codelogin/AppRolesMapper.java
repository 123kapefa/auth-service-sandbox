package sandbox.keycloak.codelogin;

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
 */
public final class AppRolesMapper extends AbstractOIDCProtocolMapper
    implements OIDCAccessTokenMapper, UserInfoTokenMapper, TokenIntrospectionTokenMapper {

    public static final String PROVIDER_ID = "sandbox-app-roles";
    public static final String ROLES_CLIENT = "roles.client";
    public static final String MODE_ATTRIBUTE = "mode.attribute";
    private static final String DEFAULT_MODE_ATTRIBUTE = "active_mode";

    private static final List<ProviderConfigProperty> CONFIG = new ArrayList<>();

    static {
        ProviderConfigProperty client = new ProviderConfigProperty();
        client.setName(ROLES_CLIENT);
        client.setLabel("Клиент приложения");
        client.setHelpText("Клиент, роли которого — роли и права приложения (например, sandbox-api).");
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
        List<String> roles = AppRoles.rolesOf(client, user);
        String modeAttribute = mapping.getConfig().getOrDefault(MODE_ATTRIBUTE, DEFAULT_MODE_ATTRIBUTE);
        token.getOtherClaims().put("role", roles);
        token.getOtherClaims().put("available_roles", roles);
        token.getOtherClaims().put("permissions", AppRoles.permissionsOf(client, user, user.getFirstAttribute(modeAttribute)));
    }
}
