package platform.keycloak.auth;

import org.keycloak.models.SingleUseObjectProvider;

/**
 * Атомарный счётчик поверх общего хранилища Keycloak: каждое событие занимает свой «слот» — ключ base:n — через
 * putIfAbsent. putIfAbsent атомарен во всём кластере и пишет сразу, а обычный put откладывается до конца запроса и
 * перезаписывает чужие значения: на нём одновременные запросы теряют друг друга (подбор кода, обход лимитов и бюджета).
 * Слоты одного base живут одинаково (lifespan) и занимаются подряд с 1, поэтому сколько занято — двоичным поиском.
 */
final class AtomicSlots {

    /** Без верхней границы — только посчитать. */
    static final int UNBOUNDED = Integer.MAX_VALUE;

    // Дальше не ищем: столько событий за сутки у одного ключа не бывает.
    private static final int MAX_SEARCH = 1 << 30;

    private final SingleUseObjectProvider store;

    AtomicSlots(SingleUseObjectProvider store) {
        this.store = store;
    }

    /**
     * Занять следующий свободный слот не выше max; вернуть его номер (1..max) или 0 — все заняты.
     * Номер — порядковый: n-е событие за время жизни слотов.
     */
    int claim(String base, long lifespanSeconds, int max) {
        for (int n = Math.max(1, taken(base) + 1); n <= max && n > 0; n++) {
            if (store.putIfAbsent(slot(base, n), Math.max(1, lifespanSeconds))) {
                return n;
            }
        }
        return 0;
    }

    /** Освободить слот (событие не состоялось). */
    void release(String base, int n) {
        if (n > 0) {
            store.remove(slot(base, n));
        }
    }

    /** Сколько слотов занято (номер последнего занятого подряд). */
    int taken(String base) {
        if (!store.contains(slot(base, 1))) {
            return 0;
        }
        // Удвоением — до первого свободного (low занят, high свободен), затем двоичный поиск границы.
        int low = 1;
        int high = 2;
        while (store.contains(slot(base, high))) {
            if (high >= MAX_SEARCH) {
                return high;
            }
            low = high;
            high *= 2;
        }
        while (high - low > 1) {
            int middle = low + (high - low) / 2;
            if (store.contains(slot(base, middle))) {
                low = middle;
            } else {
                high = middle;
            }
        }
        return low;
    }

    private static String slot(String base, int n) {
        return base + ":" + n;
    }
}
