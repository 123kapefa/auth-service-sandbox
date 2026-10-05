package platform.keycloak.auth;

import org.keycloak.broker.oidc.OAuth2IdentityProviderConfig;
import org.keycloak.models.IdentityProviderModel;

/**
 * Настройки провайдера «Госуслуги». Кроме client_id (мнемоника системы в ЕСИА) и адресов:
 * <ul>
 *   <li>apiUrl — REST API ЕСИА (данные человека и контакты);</li>
 *   <li>signerUrl — сервис подписи по ГОСТ: POST текста → подпись base64 (ключ и сертификат системы — у него);</li>
 *   <li>certificateHash — client_certificate_hash: хэш сертификата системы, считается один раз утилитой ЕСИА.</li>
 * </ul>
 * client_secret не используется: вместо секрета — подпись каждого запроса.
 */
public final class EsiaConfig extends OAuth2IdentityProviderConfig {

    static final String API_URL = "apiUrl";
    static final String SIGNER_URL = "signerUrl";
    static final String CERTIFICATE_HASH = "certificateHash";

    static final String DEFAULT_BASE = "https://esia.gosuslugi.ru";

    public EsiaConfig() {
    }

    public EsiaConfig(IdentityProviderModel model) {
        super(model);
    }

    public String getApiUrl() {
        String value = getConfig().get(API_URL);
        return YandexIdentityProvider.isBlank(value) ? DEFAULT_BASE : value.replaceAll("/+$", "");
    }

    public String getSignerUrl() {
        return getConfig().get(SIGNER_URL);
    }

    public String getCertificateHash() {
        return getConfig().get(CERTIFICATE_HASH);
    }
}
