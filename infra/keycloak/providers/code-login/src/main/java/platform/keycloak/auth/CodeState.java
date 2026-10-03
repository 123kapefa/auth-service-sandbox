package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;
import java.util.HashMap;
import java.util.Map;
import java.util.stream.Stream;

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

    /** В хранилище Keycloak (SingleUseObjectProvider) — строки: ключ → значение. */
    public Map<String, String> toNotes() {
        Map<String, String> notes = new HashMap<>();
        if (code != null) {
            notes.put("code", code);
        }
        putInstant(notes, "expiresAt", expiresAt);
        putInstant(notes, "resendAllowedAt", resendAllowedAt);
        putInstant(notes, "lockedUntil", lockedUntil);
        notes.put("failedAttempts", Integer.toString(failedAttempts));
        return notes;
    }

    /** Из хранилища; нет записи (null) — кода ещё не было. */
    public static CodeState fromNotes(Map<String, String> notes) {
        if (notes == null) {
            return EMPTY;
        }
        return new CodeState(
            notes.get("code"),
            instant(notes, "expiresAt"),
            instant(notes, "resendAllowedAt"),
            Integer.parseInt(notes.getOrDefault("failedAttempts", "0")),
            instant(notes, "lockedUntil"));
    }

    /** Сколько секунд хранить запись: пока действует код, пауза перед повтором или блокировка. Не меньше секунды. */
    public long secondsToKeep(Instant now) {
        return Stream.of(expiresAt, resendAllowedAt, lockedUntil)
            .filter(moment -> moment != null && moment.isAfter(now))
            .mapToLong(moment -> (Duration.between(now, moment).toMillis() + 999) / 1000)
            .max()
            .orElse(1);
    }

    private static void putInstant(Map<String, String> notes, String key, Instant value) {
        if (value != null) {
            notes.put(key, Long.toString(value.toEpochMilli()));
        }
    }

    private static Instant instant(Map<String, String> notes, String key) {
        String value = notes.get(key);
        return value == null ? null : Instant.ofEpochMilli(Long.parseLong(value));
    }
}
