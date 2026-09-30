package sandbox.keycloak.codelogin;

/** Куда уходит код: SMS, почта. */
interface CodeSender {

    void send(String target, String code);
}
