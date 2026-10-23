package app.xingshu.installer;

import java.util.Locale;

/** UI-thread state. Times come from a monotonic clock, never the wall clock. */
final class DownloadProgress {
    private final long started, total;
    private long bytes, lastData;
    DownloadProgress(long total, long now) {
        this.total = total;
        started = lastData = now;
    }
    void received(long count, long now) {
        if (count > bytes) lastData = now;
        bytes = Math.max(bytes, Math.min(total, count));
    }
    int percent() { return total > 0 ? (int)(bytes * 100 / total) : 0; }
    String description(long now) {
        long elapsed = Math.max(0, now - started), waiting = Math.max(0, now - lastData) / 1000;
        String speed = elapsed < 1000 ? "正在计算速度" : "平均 " + size(bytes * 1000.0 / Math.max(1, elapsed)) + "/s";
        String value = percent() + "% · " + size(bytes) + " / " + size(total) + " · " + speed;
        return waiting >= 10 ? value + "\n已 " + waiting + " 秒未收到新数据，正在等待网络…" : value;
    }
    private static String size(double bytes) {
        return bytes >= 1048576 ? String.format(Locale.SIMPLIFIED_CHINESE, "%.2f MB", bytes / 1048576)
                : String.format(Locale.SIMPLIFIED_CHINESE, "%.1f KB", bytes / 1024);
    }
}
