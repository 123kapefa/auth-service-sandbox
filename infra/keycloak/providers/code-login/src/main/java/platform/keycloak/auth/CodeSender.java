package platform.keycloak.auth;

import org.keycloak.authentication.AuthenticationFlowContext;

/** Куда уходит код: SMS, почта. */
interface CodeSender {

    /** false — отправить не удалось (код не сохраняется, пауза перед повтором не начинается). */
    boolean send(AuthenticationFlowContext context, String target, String code);
}
