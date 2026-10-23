package app.xingshu.installer;

import org.junit.Test;
import java.net.SocketTimeoutException;
import java.util.concurrent.atomic.AtomicLong;
import static org.junit.Assert.*;

public final class CheckDeadlineTest {
    @Test public void requestsAndRedirectsUseOneRemainingBudget() throws Exception {
        AtomicLong clock=new AtomicLong(1000);
        CheckDeadline budget=new CheckDeadline(clock::get,45_000_000_000L);
        assertEquals(10000,budget.limitMillis(10000));
        clock.addAndGet(43_000_000_000L);
        assertEquals(2000,budget.limitMillis(10000));
        clock.addAndGet(1_000_000_000L);
        assertEquals(1000,budget.limitMillis(15000));
    }
    @Test public void expiredCheckCannotStartAnotherRequest() throws Exception {
        AtomicLong clock=new AtomicLong(1000);
        CheckDeadline budget=new CheckDeadline(clock::get,45_000_000_000L);
        clock.addAndGet(45_000_000_000L);
        try { budget.limitMillis(15000); fail("An expired check must stop"); }
        catch(SocketTimeoutException expected) { assertTrue(expected.getMessage().contains("等待过久")); }
    }
}
