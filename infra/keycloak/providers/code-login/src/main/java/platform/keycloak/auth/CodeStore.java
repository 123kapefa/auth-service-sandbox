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

    private static final String PREFIX = "platform-code-login:";

    private final SingleUseObjectProvider store;
    private final String realmId;

    CodeStore(KeycloakSession session, RealmModel realm) {
        this.store = session.singleUseObjects();
        this.realmId = realm.getId();
    }

    CodeState load(CodeTarget target) {
        return CodeState.fromNotes(store.get(key(target)));
    }

    void save(CodeTarget target, CodeState state, Instant now) {
        store.put(key(target), state.secondsToKeep(now), state.toNotes());
    }

    private String key(CodeTarget target) {
        return PREFIX + realmId + ":" + target.channel() + ":" + target.value();
    }
}
