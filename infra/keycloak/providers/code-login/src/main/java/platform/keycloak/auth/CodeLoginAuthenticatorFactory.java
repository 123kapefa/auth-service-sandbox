package platform.keycloak.auth;

import java.security.SecureRandom;
import java.time.Clock;
import java.util.List;

import org.jboss.logging.Logger;
import org.keycloak.Config;
import org.keycloak.authentication.Authenticator;
import org.keycloak.authentication.AuthenticatorFactory;
import org.keycloak.models.AuthenticationExecutionModel.Requirement;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.models.RealmModel;
import org.keycloak.provider.ProviderConfigProperty;

/**
 * Вход по одноразовому коду (см. CodeLoginAuthenticator). Параметры кода — как в боевом сервисе (CodePolicy.PRODUCTION).
 *
 * Защита от накрутки (CodeLimitPolicy): у каждого ключа значение берётся из атрибута realm «platform.codes.ключ»
 * (настройки проекта, их пишет API админки — 10.7), иначе из настроек плагина Keycloak, иначе — по умолчанию.
 * Настройки плагина — переменные KC_SPI_AUTHENTICATOR__PLATFORM_CODE_LOGIN__КЛЮЧ (например, …__IP_PER_HOUR=1000);
 * там же капча: CAPTCHA_SERVER_KEY и CAPTCHA_SITE_KEY (Яндекс SmartCaptcha) или CAPTCHA_STUB=true (только стенд),
 * и телефония: PHONE_PROVIDER_URL (HttpPhoneProvider; нет — звонки и SMS пишутся в лог, LogPhoneProvider).
 */
public final class CodeLoginAuthenticatorFactory implements AuthenticatorFactory {

    public static final String PROVIDER_ID = "platform-code-login";

    private static final Requirement[] REQUIREMENTS = {Requirement.REQUIRED, Requirement.DISABLED};
    private static final Logger LOG = Logger.getLogger(CodeLoginAuthenticatorFactory.class);
    private static final String SMART_CAPTCHA_URL = "https://smartcaptcha.yandexcloud.net/validate";

    private Config.Scope config;
    private CaptchaVerifier captcha = CaptchaVerifier.NONE;
    private CodeMetrics metrics;
    private String captchaSiteKey;
    private PhoneProvider phone = new LogPhoneProvider();

    @Override
    public String getId() {
        return PROVIDER_ID;
    }

    @Override
    public String getDisplayType() {
        return "Sandbox: вход по коду (SMS / почта)";
    }

    @Override
    public String getHelpText() {
        return "Одноразовый код на телефон (звонок, затем SMS) или почту.";
    }

    @Override
    public String getReferenceCategory() {
        return "code-login";
    }

    @Override
    public Requirement[] getRequirementChoices() {
        return REQUIREMENTS;
    }

    @Override
    public boolean isConfigurable() {
        return false;
    }

    @Override
    public boolean isUserSetupAllowed() {
        return false;
    }

    @Override
    public List<ProviderConfigProperty> getConfigProperties() {
        return List.of();
    }

    @Override
    public Authenticator create(KeycloakSession session) {
        return new CodeLoginAuthenticator(dispatcher(), new EmailCodeSender(CodePolicy.PRODUCTION));
    }

    /** Отправка кодов с настройками кодов (лимиты, капча, телефония) — и для смены номера (UpdatePhoneAction). */
    CodeDispatcher dispatcher() {
        Clock clock = Clock.systemUTC();
        return new CodeDispatcher(new CodeRules(CodePolicy.PRODUCTION, clock, new SecureRandom()), this::limits,
            captcha, captchaSiteKey, metrics, phone, clock);
    }

    /** Защита от накрутки проекта: настройки проекта (атрибуты realm) поверх настроек сервера и значений по умолчанию. */
    CodeLimits limits(RealmModel realm) {
        return new CodeLimits(policy(realm));
    }

    CodeLimitPolicy policy(RealmModel realm) {
        return CodeLimitPolicy.from(key -> setting(realm, key), CodeLimitPolicy.DEFAULTS);
    }

    private String setting(RealmModel realm, String key) {
        // Пустой атрибут — «не задано в проекте»: так настройку проекта снимают (check-code-limits.sh).
        String value = realm.getAttribute(CodeLimitPolicy.REALM_ATTRIBUTE_PREFIX + key);
        return value != null && !value.isBlank() ? value : config.get(key);
    }

    @Override
    public void init(Config.Scope config) {
        this.config = config;
        // Проверяем настройки при старте: с опечаткой в лимитах Keycloak не поднимется, а не пропустит коды.
        CodeLimitPolicy.from(config::get, CodeLimitPolicy.DEFAULTS);
        metrics = CodeMetrics.global();
        String serverKey = config.get("captcha-server-key");
        if (serverKey != null && !serverKey.isBlank()) {
            captcha = CaptchaVerifier.smartCaptcha(serverKey, config.get("captcha-url", SMART_CAPTCHA_URL), metrics);
        } else if (config.getBoolean("captcha-stub", false)) {
            captcha = CaptchaVerifier.STUB;
            LOG.warn("Капча кодов — заглушка стенда (captcha-stub): в бою нужен captcha-server-key");
        } else {
            LOG.warn("Капча кодов не настроена (captcha-server-key): после порога коды не отправляются");
        }
        String siteKey = config.get("captcha-site-key");
        captchaSiteKey = siteKey == null || siteKey.isBlank() ? null : siteKey;
        if (captchaSiteKey == null && captcha == CaptchaVerifier.STUB) {
            // Тема рисует вместо виджета Яндекса галочку заглушки (её токен — STUB_TOKEN).
            captchaSiteKey = CaptchaVerifier.STUB_SITE_KEY;
        }
        String phoneUrl = config.get("phone-provider-url");
        if (phoneUrl != null && !phoneUrl.isBlank()) {
            phone = new HttpPhoneProvider(phoneUrl);
        }
    }

    /** Телефония для уведомлений (AccountNoticeResource): та же, что у кодов. */
    PhoneProvider phone() {
        return phone;
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
        // зависимостей от других провайдеров пока нет
    }

    @Override
    public void close() {
        // ресурсов нет
    }
}
