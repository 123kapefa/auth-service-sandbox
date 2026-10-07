package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.Map;
import java.util.function.LongToIntFunction;

import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.SingleUseObjectProvider;

/**
 * Счётчики защиты от накрутки — в том же общем хранилище Keycloak, что и CodeStore (в кластере — общие для узлов):
 * отправки на номер, с IP, на диапазон, расход проекта за сутки и сколько кодов отправлено и введено за сутки.
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

    /**
     * Сутки проекта (10.7, «Сегодня» в админке): бюджет, сколько кодов отправлено каждым способом, во что обошлось и
     * сколько введено верно.
     */
    record Day(Budget budget, Map<CodeDelivery, Long> sent, Map<CodeDelivery, Long> cost, long verified) {
        static final Day EMPTY = new Day(Budget.EMPTY, Map.of(), Map.of(), 0);

        long sent(CodeDelivery delivery) {
            return sent.getOrDefault(delivery, 0L);
        }

        long cost(CodeDelivery delivery) {
            return cost.getOrDefault(delivery, 0L);
        }

        Day plusSend(CodeDelivery delivery, long price, int alertedPercent) {
            Map<CodeDelivery, Long> nextSent = new EnumMap<>(CodeDelivery.class);
            nextSent.putAll(sent);
            nextSent.merge(delivery, 1L, Long::sum);
            Map<CodeDelivery, Long> nextCost = new EnumMap<>(CodeDelivery.class);
            nextCost.putAll(cost);
            if (price > 0) {
                nextCost.merge(delivery, price, Long::sum);
            }
            return new Day(new Budget(budget.spent() + price, Math.max(budget.alertedPercent(), alertedPercent)),
                nextSent, nextCost, verified);
        }

        Map<String, String> toNotes() {
            Map<String, String> notes = new HashMap<>();
            notes.put("spent", Long.toString(budget.spent()));
            notes.put("alertedPercent", Integer.toString(budget.alertedPercent()));
            notes.put("verified", Long.toString(verified));
            sent.forEach((delivery, count) -> notes.put("sent." + delivery, Long.toString(count)));
            cost.forEach((delivery, price) -> notes.put("cost." + delivery, Long.toString(price)));
            return notes;
        }

        static Day fromNotes(Map<String, String> notes) {
            if (notes == null) {
                return EMPTY;
            }
            Map<CodeDelivery, Long> sent = new EnumMap<>(CodeDelivery.class);
            Map<CodeDelivery, Long> cost = new EnumMap<>(CodeDelivery.class);
            for (CodeDelivery delivery : CodeDelivery.values()) {
                String count = notes.get("sent." + delivery);
                if (count != null) {
                    sent.put(delivery, Long.parseLong(count));
                }
                String price = notes.get("cost." + delivery);
                if (price != null) {
                    cost.put(delivery, Long.parseLong(price));
                }
            }
            return new Day(
                new Budget(Long.parseLong(notes.getOrDefault("spent", "0")), Integer.parseInt(notes.getOrDefault("alertedPercent", "0"))),
                sent, cost, Long.parseLong(notes.getOrDefault("verified", "0")));
        }
    }

    CodeLimits.Usage usage(CodeTarget target, String ip, String range, LocalDate today) {
        return new CodeLimits.Usage(
            history(targetKey(target)),
            history(ipKey(ip)),
            range == null ? SendHistory.EMPTY : history(rangeKey(range)),
            budget(today).spent());
    }

    /**
     * Записать отправку и её цену. alertPercent(расход после) — порог оповещения при таком расходе (CodeLimits);
     * возвращает порог, о котором пора оповестить (0 — не пора). Расход и отметка об оповещении пишутся одной записью:
     * дважды положить один ключ за запрос Infinispan не даёт («task in progress»).
     */
    int recordSend(CodeTarget target, String ip, String range, CodeDelivery delivery, long cost, LocalDate today,
                   Instant now, LongToIntFunction alertPercent) {
        append(targetKey(target), CodeLimits.DAY, now);
        append(ipKey(ip), CodeLimits.HOUR, now);
        if (range != null) {
            append(rangeKey(range), CodeLimits.HOUR, now);
        }
        if (delivery == null) {
            // код не ушёл (способ выключен у человека): лимиты считаем, в расход и отправки сутки — нет
            return 0;
        }
        Day before = day(today);
        int percent = cost == 0 ? 0 : alertPercent.applyAsInt(before.budget().spent() + cost);
        saveDay(today, before.plusSend(delivery, cost, percent));
        return percent > before.budget().alertedPercent() ? percent : 0;
    }

    /** Код введён верно — для доли введённых за сутки. */
    void recordVerified(LocalDate today) {
        Day current = day(today);
        saveDay(today, new Day(current.budget(), current.sent(), current.cost(), current.verified() + 1));
    }

    /** Отметить оповещение о пороге; true — о нём ещё не оповещали (пора). */
    boolean markAlerted(LocalDate today, int percent) {
        Day current = day(today);
        if (current.budget().alertedPercent() >= percent) {
            return false;
        }
        saveDay(today, new Day(new Budget(current.budget().spent(), percent), current.sent(), current.cost(), current.verified()));
        return true;
    }

    Budget budget(LocalDate day) {
        return day(day).budget();
    }

    Day day(LocalDate day) {
        return Day.fromNotes(store.get(budgetKey(day)));
    }

    private void saveDay(LocalDate day, Day value) {
        store.put(budgetKey(day), BUDGET_KEEP_SECONDS, value.toNotes());
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
