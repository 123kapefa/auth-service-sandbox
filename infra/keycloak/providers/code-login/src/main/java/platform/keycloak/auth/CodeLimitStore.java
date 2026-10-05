package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.util.Map;

import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.SingleUseObjectProvider;

/**
 * Счётчики защиты от накрутки — в том же общем хранилище Keycloak, что и CodeStore (в кластере — общие для узлов):
 * отправки на номер, с IP, на диапазон и расход проекта за сутки.
 * Чтение и запись не атомарны: при одновременных запросах лимит может пропустить лишнюю отправку — это допустимо,
 * лимиты защищают от потока, а не от пары запросов.
 */
final class CodeLimitStore {

    private static final String PREFIX = "platform-code-limits:";
    // Бюджет хранится двое суток: вчерашний расход ещё виден до утра (метрики, 10.7).
    private static final long BUDGET_KEEP_SECONDS = Duration.ofDays(2).toSeconds();

    private final SingleUseObjectProvider store;
    private final String realmId;

    CodeLimitStore(KeycloakSession session, RealmModel realm) {
        this.store = session.singleUseObjects();
        this.realmId = realm.getId();
    }

    /** Расход за сутки: копейки и о какой доле бюджета уже оповестили (0 — ни о какой, 80, 100). */
    record Budget(long spent, int alertedPercent) {
        static final Budget EMPTY = new Budget(0, 0);
    }

    CodeLimits.Usage usage(CodeTarget target, String ip, String range, LocalDate today) {
        return new CodeLimits.Usage(
            history(targetKey(target)),
            history(ipKey(ip)),
            range == null ? SendHistory.EMPTY : history(rangeKey(range)),
            budget(today).spent());
    }

    /** Записать отправку; возвращает расход до неё. */
    Budget recordSend(CodeTarget target, String ip, String range, long cost, LocalDate today, Instant now) {
        append(targetKey(target), CodeLimits.DAY, now);
        append(ipKey(ip), CodeLimits.HOUR, now);
        if (range != null) {
            append(rangeKey(range), CodeLimits.HOUR, now);
        }
        Budget before = budget(today);
        if (cost > 0) {
            saveBudget(today, new Budget(before.spent() + cost, before.alertedPercent()));
        }
        return before;
    }

    void markAlerted(LocalDate today, int percent) {
        Budget current = budget(today);
        saveBudget(today, new Budget(current.spent(), Math.max(current.alertedPercent(), percent)));
    }

    Budget budget(LocalDate day) {
        Map<String, String> notes = store.get(budgetKey(day));
        if (notes == null) {
            return Budget.EMPTY;
        }
        return new Budget(Long.parseLong(notes.getOrDefault("spent", "0")), Integer.parseInt(notes.getOrDefault("alertedPercent", "0")));
    }

    private void saveBudget(LocalDate day, Budget budget) {
        store.put(budgetKey(day), BUDGET_KEEP_SECONDS,
            Map.of("spent", Long.toString(budget.spent()), "alertedPercent", Integer.toString(budget.alertedPercent())));
    }

    private SendHistory history(String key) {
        return SendHistory.fromNotes(store.get(key));
    }

    private void append(String key, Duration keep, Instant now) {
        SendHistory next = history(key).plus(now, keep);
        store.put(key, keep.toSeconds(), next.toNotes());
    }

    private String targetKey(CodeTarget target) {
        return PREFIX + realmId + ":target:" + target.channel() + ":" + target.value();
    }

    private String ipKey(String ip) {
        return PREFIX + realmId + ":ip:" + ip;
    }

    private String rangeKey(String range) {
        return PREFIX + realmId + ":range:" + range;
    }

    private String budgetKey(LocalDate day) {
        return PREFIX + realmId + ":budget:" + day;
    }
}
