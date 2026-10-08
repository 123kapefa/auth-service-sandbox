package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;

import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.SingleUseObjectProvider;

/**
 * Состояние кода по номеру — в общем хранилище Keycloak (SingleUseObjectProvider; в кластере — общее для всех узлов).
 * Ключ — realm + канал + номер или почта: лимиты не зависят от попытки входа, браузера и вкладки.
 */
final class CodeStore {

    /** Коды входа. */
    static final String LOGIN = "platform-code-login:";
    /** Коды смены номера: код входа на тот же номер для смены не годится, и наоборот. */
    static final String PHONE_CHANGE = "platform-phone-change:";

    // Сколько держится занятость отправки: дольше любого звонка провайдера, короче любой паузы перед повтором.
    private static final long SEND_CLAIM_SECONDS = 30;

    private final SingleUseObjectProvider store;
    private final AtomicSlots slots;
    private final String realmId;
    private final String prefix;

    CodeStore(KeycloakSession session, RealmModel realm) {
        this(session, realm, LOGIN);
    }

    CodeStore(KeycloakSession session, RealmModel realm, String prefix) {
        this.store = session.singleUseObjects();
        this.slots = new AtomicSlots(store);
        this.realmId = realm.getId();
        this.prefix = prefix;
    }

    CodeState load(CodeTarget target) {
        return CodeState.fromNotes(store.get(key(target)));
    }

    void save(CodeTarget target, CodeState state, Instant now) {
        store.put(key(target), state.secondsToKeep(now), state.toNotes());
    }

    /**
     * Номер попытки ввода к действующему коду state (1..max) или 0 — попытки к нему кончились. Атомарно для всех
     * узлов (AtomicSlots): одновременные вводы получают разные номера.
     */
    int claimAttempt(CodeTarget target, CodeState state, int max, Instant now) {
        long lifespan = (Duration.between(now, state.expiresAt()).toMillis() + 999) / 1000;
        return slots.claim(key(target) + ":attempt:" + state.expiresAt().toEpochMilli(), lifespan, max);
    }

    /**
     * Отправка на этот номер уже идёт (другой запрос занял её для того же состояния кода)? Занять; true — заняли мы.
     * Без этого одновременные «Отправить ещё раз» проходят паузу все сразу: несколько звонков и SMS на один номер.
     */
    boolean claimSend(CodeTarget target, CodeState state) {
        String version = state.resendAllowedAt() == null ? "none" : Long.toString(state.resendAllowedAt().toEpochMilli());
        return slots.claim(key(target) + ":send:" + version, SEND_CLAIM_SECONDS, 1) == 1;
    }

    /** Отправки не было (отказ лимита или бюджета до провайдера) — можно пробовать снова, не дожидаясь срока. */
    void releaseSend(CodeTarget target, CodeState state) {
        String version = state.resendAllowedAt() == null ? "none" : Long.toString(state.resendAllowedAt().toEpochMilli());
        slots.release(key(target) + ":send:" + version, 1);
    }

    private String key(CodeTarget target) {
        return prefix + realmId + ":" + target.channel() + ":" + target.value();
    }
}
