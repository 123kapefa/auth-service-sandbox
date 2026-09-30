package sandbox.keycloak.codelogin;

import java.time.Instant;

/**
 * Что известно о коде для одного номера (или почты). Хранится по номеру, а не по попытке входа:
 * начать вход заново и обойти лимиты нельзя.
 *
 * @param code            действующий код или null
 * @param expiresAt       когда код перестаёт действовать
 * @param resendAllowedAt раньше этого времени новый код не отправляется
 * @param failedAttempts  неверных вводов подряд
 * @param lockedUntil     до этого времени ни отправить, ни ввести код нельзя (или null)
 */
public record CodeState(String code, Instant expiresAt, Instant resendAllowedAt, int failedAttempts, Instant lockedUntil) {

    public static final CodeState EMPTY = new CodeState(null, null, null, 0, null);
}
