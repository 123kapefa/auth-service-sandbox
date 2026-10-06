package platform.keycloak.auth;

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

    private final SingleUseObjectProvider store;
    private final String realmId;
    private final String prefix;

    CodeStore(KeycloakSession session, RealmModel realm) {
        this(session, realm, LOGIN);
    }

    CodeStore(KeycloakSession session, RealmModel realm, String prefix) {
        this.store = session.singleUseObjects();
        this.realmId = realm.getId();
        this.prefix = prefix;
    }

    CodeState load(CodeTarget target) {
        return CodeState.fromNotes(store.get(key(target)));
    }

    void save(CodeTarget target, CodeState state, Instant now) {
        store.put(key(target), state.secondsToKeep(now), state.toNotes());
    }

    private String key(CodeTarget target) {
        return prefix + realmId + ":" + target.channel() + ":" + target.value();
    }
}
