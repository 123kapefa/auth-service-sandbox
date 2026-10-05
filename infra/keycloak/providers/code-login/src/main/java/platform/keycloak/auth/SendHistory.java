package platform.keycloak.auth;

import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Map;

/**
 * Моменты отправок кода на один номер, с одного IP или на один диапазон — для лимитов «столько-то за час/сутки».
 * Окно скользящее: считаются отправки за последний час, а не с начала часа.
 *
 * @param sentAt моменты отправок по возрастанию
 */
public record SendHistory(List<Instant> sentAt) {

    public static final SendHistory EMPTY = new SendHistory(List.of());

    // Не храним больше: лимиты меньше, а запись в хранилище должна оставаться маленькой.
    static final int MAX_ENTRIES = 200;

    public SendHistory {
        sentAt = List.copyOf(sentAt);
    }

    /** Сколько отправок за последнее окно. */
    public int count(Instant now, Duration window) {
        Instant from = now.minus(window);
        return (int) sentAt.stream().filter(moment -> moment.isAfter(from)).count();
    }

    /**
     * Лимит limit отправок за окно уже выбран — через сколько секунд освободится место (0 — не выбран).
     * Место освобождается, когда из окна выходит limit-я с конца отправка.
     */
    public long secondsUntilFree(Instant now, Duration window, int limit) {
        List<Instant> recent = sentAt.stream().filter(moment -> moment.isAfter(now.minus(window))).toList();
        if (recent.size() < limit) {
            return 0;
        }
        Instant leaves = recent.get(recent.size() - limit).plus(window);
        return Math.max(1, (Duration.between(now, leaves).toMillis() + 999) / 1000);
    }

    /** С новой отправкой; отправки старше keep выбрасываются. */
    public SendHistory plus(Instant now, Duration keep) {
        List<Instant> next = new ArrayList<>(sentAt.stream().filter(moment -> moment.isAfter(now.minus(keep))).toList());
        next.add(now);
        return new SendHistory(next.size() > MAX_ENTRIES ? next.subList(next.size() - MAX_ENTRIES, next.size()) : next);
    }

    /** В хранилище Keycloak — строки: моменты в миллисекундах через запятую. */
    public Map<String, String> toNotes() {
        return Map.of("sentAt", String.join(",", sentAt.stream().map(moment -> Long.toString(moment.toEpochMilli())).toList()));
    }

    public static SendHistory fromNotes(Map<String, String> notes) {
        String value = notes == null ? null : notes.get("sentAt");
        if (value == null || value.isEmpty()) {
            return EMPTY;
        }
        return new SendHistory(Arrays.stream(value.split(",")).map(item -> Instant.ofEpochMilli(Long.parseLong(item))).sorted().toList());
    }
}
