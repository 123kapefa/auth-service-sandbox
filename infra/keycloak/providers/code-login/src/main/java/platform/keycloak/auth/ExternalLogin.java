package platform.keycloak.auth;

import com.fasterxml.jackson.databind.JsonNode;

import org.keycloak.broker.oidc.mappers.AbstractJsonUserAttributeMapper;
import org.keycloak.broker.provider.BrokeredIdentityContext;
import org.keycloak.broker.provider.IdentityProvider;
import org.keycloak.models.IdentityProviderModel;
import org.keycloak.models.UserModel;

/**
 * Общее у входа через Яндекс ID и Госуслуги: человек → контекст входа Keycloak и новый пользователь.
 * Логин нового пользователя — телефон цифрами: Keycloak сам находит пользователя с тем же логином (или той же почтой)
 * и предлагает привязать вход к нему, подтвердив паролем, — так человек не получает второй аккаунт.
 */
final class ExternalLogin {

    private ExternalLogin() {
    }

    static BrokeredIdentityContext context(ExternalUser user, JsonNode rawProfile, IdentityProviderModel config, IdentityProvider<?> idp) {
        BrokeredIdentityContext context = new BrokeredIdentityContext(user.id(), config);
        context.setUsername(user.username(config.getAlias()));
        context.setModelUsername(user.username(config.getAlias()));
        context.setEmail(user.email());
        context.setFirstName(user.firstName());
        context.setLastName(user.lastName());
        context.setIdp(idp);
        // Весь ответ провайдера — для стандартных мапперов атрибутов («Attribute Importer» в админке Keycloak).
        AbstractJsonUserAttributeMapper.storeUserProfileForMapper(context, rawProfile, config.getAlias());
        return context;
    }

    /** Новый пользователь: телефон (логин из цифр) — в атрибут phone, как у пользователей, заведённых иначе. */
    static void importNewUser(UserModel user) {
        PhoneNormalizer.toLogin(user.getUsername())
            .filter(login -> login.equals(user.getUsername()))
            .ifPresent(login -> user.setSingleAttribute("phone", PhoneNormalizer.toE164(login)));
    }
}
