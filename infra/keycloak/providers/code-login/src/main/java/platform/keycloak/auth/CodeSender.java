package platform.keycloak.auth;

import org.keycloak.authentication.AuthenticationFlowContext;

/** Код письмом (EmailCodeSender); на телефон — PhoneProvider. */
interface CodeSender {

    /** false — отправить не удалось (код не сохраняется, пауза перед повтором не начинается). */
    boolean send(AuthenticationFlowContext context, String target, String code);
}
