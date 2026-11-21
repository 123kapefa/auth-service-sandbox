package platform.keycloak.auth;

import java.util.ArrayList;
import java.util.List;

/**
 * Заглушки стенда (captcha-stub, phone-stub: код в лог, капча «галочкой») разрешены только в режиме разработки Keycloak
 * (start-dev) или при явном allow-stubs=true. Иначе флаг, перенесённый из стенда (kind, compose) в бой, не пройдёт
 * молча, а остановит старт: заглушка капчи отменяет защиту от накрутки, запись кодов в лог — вход под любым номером.
 */
final class StubPolicy {

    private StubPolicy() {
    }

    /**
     * @param devMode      Keycloak запущен как start-dev (Environment.isDevMode()).
     * @param allowStubs   allow-stubs=true: оператор явно разрешил заглушки вне start-dev (kind, закрытый стенд).
     * @throws IllegalStateException включена заглушка, а ни режима разработки, ни allow-stubs нет.
     */
    static void require(boolean devMode, boolean allowStubs, boolean captchaStub, boolean phoneStub) {
        if (devMode || allowStubs || (!captchaStub && !phoneStub)) {
            return;
        }
        List<String> enabled = new ArrayList<>();
        if (captchaStub) {
            enabled.add("captcha-stub");
        }
        if (phoneStub) {
            enabled.add("phone-stub");
        }
        throw new IllegalStateException("Заглушки стенда (" + String.join(", ", enabled) + ") включены не в режиме start-dev: "
            + "уберите их (в бою нужны captcha-server-key и phone-provider-url) или, для закрытого стенда, задайте allow-stubs=true "
            + "(KC_SPI_AUTHENTICATOR__PLATFORM_CODE_LOGIN__ALLOW_STUBS=true).");
    }
}
