package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.function.LongToIntFunction;

import org.keycloak.models.KeycloakSession;
import org.keycloak.models.RealmModel;
import org.keycloak.models.SingleUseObjectProvider;

/**
 * Счётчики защиты от накрутки — в том же общем хранилище Keycloak, что и CodeStore (в кластере — общие для узлов):
 * отправки на номер, с IP, на диапазон, расход проекта за сутки и сколько кодов отправлено и введено за сутки.
 * Истории отправок и запись суток пишутся обычным put и при одновременных запросах теряют друг друга — по ним только
 * подсказки экрану (растущая пауза, капча, «повтор через») и отчёт «Сегодня». Сами лимиты и бюджет держит reserve:
 * атомарные слоты (AtomicSlots) в окнах «час» и «сутки» — сколько бы запросов ни пришло разом, сверх лимита не уйдёт.
 */
final class CodeLimitStore {

    private static final String PREFIX = "platform-code-limits:";
    // Слоты лимитов и расхода (AtomicSlots) — отдельно от историй: те пишутся put, эти — только putIfAbsent.
    private static final String SLOTS = "platform-code-slots:";
    // Бюджет хранится двое суток: вчерашний расход ещё виден до утра (метрики, 10.7).
    private static final long BUDGET_KEEP_SECONDS = Duration.ofDays(2).toSeconds();

    private final SingleUseObjectProvider store;
    private final AtomicSlots slots;
    private final String realmId;

    CodeLimitStore(KeycloakSession session, RealmModel realm) {
        this(session.singleUseObjects(), realm.getId());
    }

    CodeLimitStore(SingleUseObjectProvider store, String realmId) {
        this.store = store;
        this.slots = new AtomicSlots(store);
        this.realmId = realmId;
    }

    /**
     * Атомарно занять отправку во всех лимитах: на номер за час и за сутки, с IP за час, на диапазон за час. Окна —
     * календарные (час по UTC, сутки — в поясе бюджета). null — заняли; хоть один лимит выбран — ничего не занято,
     * ответ — какой и когда освободится. Занятое не возвращается, даже если код потом не ушёл: неудачные отправки тоже
     * расходуют лимиты (таймаут провайдера после звонка, отказ бюджета).
     */
    CodeLimits.Decision.Limited reserve(CodeTarget target, String ip, String range, CodeLimitPolicy policy, Instant now) {
        long hour = now.getEpochSecond() / 3600;
        Instant hourEnds = Instant.ofEpochSecond((hour + 1) * 3600);
        LocalDate today = LocalDate.ofInstant(now, policy.zone());
        Instant dayEnds = today.plusDays(1).atStartOfDay(policy.zone()).toInstant();
        List<Map.Entry<String, Integer>> claimed = new ArrayList<>();

        record Window(String limit, String base, int max, Instant ends) {
        }
        List<Window> windows = new ArrayList<>(List.of(
            new Window("target-per-hour", targetKey(target) + ":h" + hour, policy.targetPerHour(), hourEnds),
            new Window("target-per-day", targetKey(target) + ":d" + today, policy.targetPerDay(), dayEnds),
            new Window("ip-per-hour", ipKey(ip) + ":h" + hour, policy.ipPerHour(), hourEnds)));
        if (range != null) {
            windows.add(new Window("range-per-hour", rangeKey(range) + ":h" + hour, policy.rangePerHour(), hourEnds));
        }
        for (Window window : windows) {
            long keep = Duration.between(now, window.ends()).toSeconds() + 1;
            int n = slots.claim(SLOTS + window.base(), keep, window.max());
            if (n == 0) {
                release(claimed);
                return new CodeLimits.Decision.Limited(window.limit(), Math.max(1, keep));
            }
            claimed.add(Map.entry(SLOTS + window.base(), n));
        }
        return null;
    }

    /** Освободить занятые слоты. */
    void release(List<Map.Entry<String, Integer>> claimed) {
        claimed.forEach(slot -> slots.release(slot.getKey(), slot.getValue()));
        claimed.clear();
    }

    /**
     * Атомарно учесть отправку способом delivery в расходе суток. SMS сверх бюджета не учитывается (false — бюджет
     * кончился); звонки и почта учитываются всегда. Расход — по числу отправок каждым способом и ценам: так его не
     * теряют одновременные запросы. claimed — что занято. Неудачная отправка расход не возвращает (CodeDispatcher).
     */
    boolean reserveSpend(CodeDelivery delivery, CodeLimitPolicy policy, LocalDate today,
                         List<Map.Entry<String, Integer>> claimed) {
        if (delivery == CodeDelivery.EMAIL) {
            return true;
        }
        String base = spendKey(today, delivery);
        int n = slots.claim(base, BUDGET_KEEP_SECONDS, AtomicSlots.UNBOUNDED);
        if (n == 0) {
            return false;
        }
        // Номер своей SMS за сутки (n) уникален, поэтому и одновременные запросы не уйдут за бюджет вместе.
        long withThis = (long) n * policy.smsCost() + (long) slots.taken(spendKey(today, CodeDelivery.CALL)) * policy.callCost();
        if (delivery == CodeDelivery.SMS && policy.dailyBudget() > 0
            && Math.max(withThis, day(today).budget().spent() + policy.smsCost()) > policy.dailyBudget()) {
            slots.release(base, n);
            return false;
        }
        claimed.add(Map.entry(base, n));
        return true;
    }

    /** Расход за сутки, копейки: больший из записи суток и подсчёта по слотам (его не теряют гонки). */
    long spent(LocalDate today, CodeLimitPolicy policy) {
        long counted = (long) slots.taken(spendKey(today, CodeDelivery.SMS)) * policy.smsCost()
            + (long) slots.taken(spendKey(today, CodeDelivery.CALL)) * policy.callCost();
        return Math.max(counted, day(today).budget().spent());
    }

    /** Оповестить о пороге percent за сутки — один раз во всём кластере: true — этот запрос первый. */
    boolean claimAlert(LocalDate today, int percent) {
        return slots.claim(PREFIX + realmId + ":alert:" + today + ":" + percent, BUDGET_KEEP_SECONDS, 1) == 1;
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
     * возвращает порог, о котором пора оповестить (0 — не пора; о каждом пороге — один раз за сутки, claimAlert).
     * spentNow — расход по слотам (reserveSpend) с этой отправкой. Дважды положить один ключ за запрос Infinispan не
     * даёт («task in progress»), поэтому сутки пишутся одной записью.
     */
    int recordSend(CodeTarget target, String ip, String range, CodeDelivery delivery, long cost, long spentNow,
                   LocalDate today, Instant now, LongToIntFunction alertPercent) {
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
        int percent = cost == 0 ? 0 : alertPercent.applyAsInt(Math.max(before.budget().spent() + cost, spentNow));
        saveDay(today, before.plusSend(delivery, cost, percent));
        return percent > 0 && claimAlert(today, percent) ? percent : 0;
    }

    /** Код введён верно — для доли введённых за сутки. */
    void recordVerified(LocalDate today) {
        Day current = day(today);
        saveDay(today, new Day(current.budget(), current.sent(), current.cost(), current.verified() + 1));
    }

    /** Отметить оповещение о пороге; true — о нём ещё не оповещали (пора). */
    boolean markAlerted(LocalDate today, int percent) {
        Day current = day(today);
        if (current.budget().alertedPercent() >= percent || !claimAlert(today, percent)) {
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

    private String spendKey(LocalDate day, CodeDelivery delivery) {
        return SLOTS + realmId + ":spend:" + day + ":" + delivery;
    }

    private String budgetKey(LocalDate day) {
        return PREFIX + realmId + ":budget:" + day;
    }
}
