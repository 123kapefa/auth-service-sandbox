package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;
import java.time.format.DateTimeParseException;

/**
 * Когда после входа по коду предлагать ключ доступа (10.6), без Keycloak: ключи доступа включены, своего ключа ещё
 * нет, и «Не сейчас» нажимали не меньше 30 дней назад (или не нажимали).
 */
final class PasskeyOfferRules {

    /** Когда нажали «Не сейчас» (ISO-время); пишет только PasskeyOfferAction. */
    static final String SKIPPED_AT = "passkey_offer_skipped_at";

    static final Duration PAUSE = Duration.ofDays(30);

    private PasskeyOfferRules() {
    }

    /**
     * @param passkeysEnabled ключи доступа включены в realm
     * @param hasPasskey      у человека уже есть ключ доступа
     * @param skippedAt       атрибут SKIPPED_AT (нет или не разобрать — не пропускали)
     */
    static boolean shouldOffer(boolean passkeysEnabled, boolean hasPasskey, String skippedAt, Instant now) {
        if (!passkeysEnabled || hasPasskey) {
            return false;
        }
        if (skippedAt == null || skippedAt.isBlank()) {
            return true;
        }
        try {
            return !now.isBefore(Instant.parse(skippedAt).plus(PAUSE));
        } catch (DateTimeParseException e) {
            return true;
        }
    }
}
