package sandbox.keycloak.codelogin;

import java.time.Duration;

/**
 * Правила одноразового кода.
 *
 * @param length      цифр в коде
 * @param ttl         сколько код действует
 * @param cooldown    пауза перед повторной отправкой
 * @param maxAttempts неверных вводов до блокировки
 * @param lockout     на сколько блокируется номер после maxAttempts неверных вводов
 */
public record CodePolicy(int length, Duration ttl, Duration cooldown, int maxAttempts, Duration lockout) {

    /** Как в боевом auth-service (Otp в appsettings.json). */
    public static final CodePolicy PRODUCTION =
        new CodePolicy(6, Duration.ofSeconds(300), Duration.ofSeconds(60), 5, Duration.ofSeconds(300));
}
