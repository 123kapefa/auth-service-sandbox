package sandbox.keycloak.codelogin;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.random.RandomGenerator;

/**
 * Правила кода без Keycloak: можно ли отправить новый код и верен ли введённый.
 * Состояние не хранит — получает и возвращает CodeState; время — через Clock (тесты подставляют свои часы).
 */
public final class CodeRules {

    private final CodePolicy policy;
    private final Clock clock;
    private final RandomGenerator random;

    public CodeRules(CodePolicy policy, Clock clock, RandomGenerator random) {
        this.policy = policy;
        this.clock = clock;
        this.random = random;
    }

    public sealed interface SendResult {
        /** Код создан: отправить его и сохранить состояние. */
        record Sent(String code, CodeState state) implements SendResult { }

        /** Рано: повтор через секунды. */
        record TooEarly(long retryAfterSeconds) implements SendResult { }

        /** Номер заблокирован после неверных вводов. */
        record Locked(long retryAfterSeconds) implements SendResult { }
    }

    public sealed interface VerifyResult {
        /** Код верный: вход. Состояние сбрасывается. */
        record Success(CodeState state) implements VerifyResult { }

        /** Неверный код. */
        record Wrong(CodeState state, int attemptsLeft) implements VerifyResult { }

        /** Неверный код, попытки кончились: код сгорел, номер заблокирован. */
        record LockedNow(CodeState state, long retryAfterSeconds) implements VerifyResult { }

        /** Номер уже заблокирован. */
        record Locked(long retryAfterSeconds) implements VerifyResult { }

        /** Кода нет или он истёк: нужно запросить новый. */
        record NoActiveCode() implements VerifyResult { }
    }

    /** Сколько цифр в коде — экрану кода (подсказка и автоотправка). */
    public int codeLength() {
        return policy.length();
    }

    public SendResult send(CodeState current) {
        Instant now = clock.instant();

        if (isLocked(current, now)) {
            return new SendResult.Locked(secondsUntil(now, current.lockedUntil()));
        }

        if (current.resendAllowedAt() != null && now.isBefore(current.resendAllowedAt())) {
            return new SendResult.TooEarly(secondsUntil(now, current.resendAllowedAt()));
        }

        String code = generate();
        // Новый код сбрасывает неверные попытки — как в боевом сервисе.
        return new SendResult.Sent(code, new CodeState(code, now.plus(policy.ttl()), now.plus(policy.cooldown()), 0, null));
    }

    public VerifyResult verify(CodeState current, String input) {
        Instant now = clock.instant();

        if (isLocked(current, now)) {
            return new VerifyResult.Locked(secondsUntil(now, current.lockedUntil()));
        }

        if (current.code() == null || current.expiresAt() == null || !now.isBefore(current.expiresAt())) {
            return new VerifyResult.NoActiveCode();
        }

        if (input != null && constantTimeEquals(current.code(), input.trim())) {
            return new VerifyResult.Success(new CodeState(null, null, current.resendAllowedAt(), 0, null));
        }

        int failed = current.failedAttempts() + 1;
        if (failed >= policy.maxAttempts()) {
            Instant lockedUntil = now.plus(policy.lockout());
            return new VerifyResult.LockedNow(
                new CodeState(null, null, current.resendAllowedAt(), failed, lockedUntil),
                policy.lockout().toSeconds());
        }

        return new VerifyResult.Wrong(
            new CodeState(current.code(), current.expiresAt(), current.resendAllowedAt(), failed, null),
            policy.maxAttempts() - failed);
    }

    private String generate() {
        StringBuilder code = new StringBuilder(policy.length());
        for (int i = 0; i < policy.length(); i++) {
            code.append(random.nextInt(10));
        }
        return code.toString();
    }

    private static boolean isLocked(CodeState state, Instant now) {
        return state.lockedUntil() != null && now.isBefore(state.lockedUntil());
    }

    private static long secondsUntil(Instant now, Instant moment) {
        // Округляем вверх: «повтор через 1 с», а не «через 0 с».
        return (Duration.between(now, moment).toMillis() + 999) / 1000;
    }

    // Сравнение за постоянное время: по времени ответа нельзя угадывать код по цифрам.
    private static boolean constantTimeEquals(String expected, String actual) {
        return MessageDigest.isEqual(expected.getBytes(StandardCharsets.UTF_8), actual.getBytes(StandardCharsets.UTF_8));
    }
}
