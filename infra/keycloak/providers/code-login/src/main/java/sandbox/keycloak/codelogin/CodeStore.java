package sandbox.keycloak.codelogin;

import java.time.Instant;

import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.SingleUseObjectProvider;

/**
 * Состояние кода по номеру — в общем хранилище Keycloak (SingleUseObjectProvider; в кластере — общее для всех узлов).
 * Ключ — realm + номер: лимиты не зависят от попытки входа, браузера и вкладки.
 */
final class CodeStore {

    private static final String PREFIX = "sandbox-code-login:";

    private final SingleUseObjectProvider store;
    private final String realmId;

    CodeStore(KeycloakSession session, RealmModel realm) {
        this.store = session.singleUseObjects();
        this.realmId = realm.getId();
    }

    CodeState load(String target) {
        return CodeState.fromNotes(store.get(key(target)));
    }

    void save(String target, CodeState state, Instant now) {
        store.put(key(target), state.secondsToKeep(now), state.toNotes());
    }

    private String key(String target) {
        return PREFIX + realmId + ":" + target;
    }
}
