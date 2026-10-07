package platform.keycloak.auth;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

import org.keycloak.models.SingleUseObjectProvider;

/** Хранилище Keycloak в памяти для тестов: putIfAbsent атомарен, как у Infinispan; срок жизни не учитывается. */
final class MemorySingleUseObjects implements SingleUseObjectProvider {

    private final Map<String, Map<String, String>> values = new ConcurrentHashMap<>();

    @Override
    public void put(String key, long lifespanSeconds, Map<String, String> notes) {
        values.put(key, Map.copyOf(notes));
    }

    @Override
    public Map<String, String> get(String key) {
        return values.get(key);
    }

    @Override
    public Map<String, String> remove(String key) {
        return values.remove(key);
    }

    @Override
    public boolean replace(String key, Map<String, String> notes) {
        return values.replace(key, Map.copyOf(notes)) != null;
    }

    @Override
    public boolean putIfAbsent(String key, long lifespanInSeconds) {
        return values.putIfAbsent(key, Map.of()) == null;
    }

    @Override
    public boolean contains(String key) {
        return values.containsKey(key);
    }

    @Override
    public void close() {
    }
}
