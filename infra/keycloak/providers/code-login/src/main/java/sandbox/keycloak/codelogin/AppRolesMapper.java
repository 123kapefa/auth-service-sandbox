package sandbox.keycloak.codelogin;

import java.util.ArrayList;
import java.util.List;

import org.keycloak.models.ClientModel;
import org.keycloak.models.ClientSessionContext;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.ProtocolMapperModel;
import org.keycloak.models.UserSessionModel;
import org.keycloak.protocol.oidc.mappers.AbstractOIDCProtocolMapper;
import org.keycloak.protocol.oidc.mappers.OIDCAccessTokenMapper;
import org.keycloak.protocol.oidc.mappers.OIDCAttributeMapperHelper;
import org.keycloak.protocol.oidc.mappers.TokenIntrospectionTokenMapper;
import org.keycloak.protocol.oidc.mappers.UserInfoTokenMapper;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.representations.IDToken;

/**
 * Роли приложения в токене: claims role и available_roles — только роли (режимы) клиента приложения, без прав.
 * Стандартный маппер ролей клиента положил бы и права: роли составные, права входят в них.
 */
public final class AppRolesMapper extends AbstractOIDCProtocolMapper
    implements OIDCAccessTokenMapper, UserInfoTokenMapper, TokenIntrospectionTokenMapper {

    public static final String PROVIDER_ID = "sandbox-app-roles";
    public static final String ROLES_CLIENT = "roles.client";

    private static final List<ProviderConfigProperty> CONFIG = new ArrayList<>();

    static {
        ProviderConfigProperty client = new ProviderConfigProperty();
        client.setName(ROLES_CLIENT);
        client.setLabel("Клиент приложения");
        client.setHelpText("Клиент, роли которого — роли и права приложения (например, sandbox-api).");
        client.setType(ProviderConfigProperty.CLIENT_LIST_TYPE);
        CONFIG.add(client);
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
        return "role и available_roles — роли (режимы) приложения без прав.";
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

        List<String> roles = AppRoles.rolesOf(client, userSession.getUser());
        token.getOtherClaims().put("role", roles);
        token.getOtherClaims().put("available_roles", roles);
    }
}
