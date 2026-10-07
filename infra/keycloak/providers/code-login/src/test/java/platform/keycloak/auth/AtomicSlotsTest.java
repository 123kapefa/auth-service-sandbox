package platform.keycloak.auth;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.util.List;
import java.util.concurrent.Callable;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.stream.IntStream;

import org.junit.jupiter.api.Test;

class AtomicSlotsTest {

    private final AtomicSlots slots = new AtomicSlots(new MemorySingleUseObjects());

    @Test
    void claim_givesNumbersInOrder_upToMax() {
        assertEquals(1, slots.claim("k", 60, 3));
        assertEquals(2, slots.claim("k", 60, 3));
        assertEquals(3, slots.claim("k", 60, 3));
        assertEquals(0, slots.claim("k", 60, 3));
        assertEquals(3, slots.taken("k"));
    }

    @Test
    void release_freesTheSlot() {
        slots.claim("k", 60, 2);
        int second = slots.claim("k", 60, 2);
        slots.release("k", second);

        assertEquals(2, slots.claim("k", 60, 2));
    }

    @Test
    void taken_countsLongRuns() {
        IntStream.rangeClosed(1, 1000).forEach(i -> slots.claim("k", 60, AtomicSlots.UNBOUNDED));

        assertEquals(1000, slots.taken("k"));
        assertEquals(0, slots.taken("other"));
    }

    @Test
    void concurrentClaims_neverExceedMax() throws Exception {
        ExecutorService pool = Executors.newFixedThreadPool(16);
        try {
            List<Callable<Integer>> tasks = IntStream.range(0, 200).<Callable<Integer>>mapToObj(i -> () -> slots.claim("k", 60, 5)).toList();
            long granted = 0;
            for (Future<Integer> result : pool.invokeAll(tasks)) {
                if (result.get() > 0) {
                    granted++;
                }
            }
            assertEquals(5, granted);
        } finally {
            pool.shutdownNow();
        }
    }
}
