package app.xingshu.installer;

import java.net.SocketTimeoutException;
import java.util.function.LongSupplier;

/** One monotonic budget shared by all metadata requests and redirects in a check. */
final class CheckDeadline {
    private final LongSupplier clock;
    private final long deadline;
    CheckDeadline() { this(System::nanoTime,45_000_000_000L); }
    CheckDeadline(LongSupplier clock,long durationNanos) {
        this.clock=clock; deadline=clock.getAsLong()+durationNanos;
    }
    int limitMillis(int maximum) throws SocketTimeoutException {
        long remaining=deadline-clock.getAsLong();
        if(remaining<=0)throw new SocketTimeoutException("本次更新检查等待过久，请重试。");
        return (int)Math.max(1,Math.min(maximum,(remaining+999999)/1000000));
    }
}
