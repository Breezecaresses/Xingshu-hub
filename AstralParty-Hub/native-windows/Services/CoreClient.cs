using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace XingShu.Native.Services;

public sealed class CoreClient : IAsyncDisposable
{
    private Process? process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writes = new(1, 1);
    private long sequence;
    private readonly TaskCompletionSource<JsonElement> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Queue<string> errors = new();
    private Task? reader;
    public event Action<string, JsonElement>? Event;
    public bool IsRunning => process is { HasExited: false };
    public JsonElement Status { get; private set; }

    public async Task StartAsync(bool offline = false, string? dataDirectory = null)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Core", "xingshu-core.exe");
        if (!File.Exists(file)) throw new FileNotFoundException("找不到星枢核心，请解压完整的软件目录后运行。", file);
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = AppContext.BaseDirectory };
        info.ArgumentList.Add("--stdio");
        if (offline) info.ArgumentList.Add("--no-bridge");
        if (dataDirectory is not null) { info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(dataDirectory); }
        process = Process.Start(info) ?? throw new IOException("无法启动星枢核心。");
        _ = Task.Run(async () => { while (await process.StandardError.ReadLineAsync() is { } line)
            lock (errors) { errors.Enqueue(line); while (errors.Count > 30) errors.Dequeue(); } });
        reader = Task.Run(ReadAsync);
        Status = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private async Task ReadAsync()
    {
        Exception failure = new IOException("星枢核心已退出，请重新启动原生客户端。");
        try
        {
            while (await process!.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.Length > 8 * 1024 * 1024) throw new IOException("核心响应超出大小限制。");
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("event", out var name))
                {
                    var payload = root.GetProperty("payload").Clone();
                    if (name.GetString() == "core://ready") ready.TrySetResult(payload);
                    Event?.Invoke(name.GetString()!, payload);
                }
                else if (root.TryGetProperty("id", out var id) && pending.TryRemove(id.GetInt64(), out var task))
                {
                    if (root.GetProperty("ok").GetBoolean()) task.TrySetResult(root.GetProperty("result").Clone());
                    else task.TrySetException(new InvalidOperationException(root.GetProperty("error").GetString()));
                }
            }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            lock (errors) if (errors.Count > 0) failure = new IOException(failure.Message + "\n" + string.Join('\n', errors), failure);
            ready.TrySetException(failure);
            foreach (var entry in pending) if (pending.TryRemove(entry.Key, out var task)) task.TrySetException(failure);
            Event?.Invoke("core://disconnected", JsonSerializer.SerializeToElement(failure.Message));
        }
    }

    public async Task<JsonElement> CallAsync(string command, object? args = null, CancellationToken cancellation = default)
    {
        if (!IsRunning) throw new IOException("星枢核心尚未连接。");
        if (pending.Count >= 32) throw new IOException("操作较多，请稍候再试。");
        var id = Interlocked.Increment(ref sequence);
        var task = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = task;
        try
        {
            var request = JsonSerializer.Serialize(new { id, command, args = args ?? new { } });
            await writes.WaitAsync(cancellation);
            try { await process!.StandardInput.WriteLineAsync(request.AsMemory(), cancellation); await process.StandardInput.FlushAsync(cancellation); }
            finally { writes.Release(); }
            return await task.Task.WaitAsync(TimeSpan.FromSeconds(50), cancellation);
        }
        finally { pending.TryRemove(id, out _); }
    }

    public async ValueTask DisposeAsync()
    {
        if (process is null) return;
        try { process.StandardInput.Close(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } }
        if (reader is not null) try { await reader.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
        process.Dispose(); process = null;
    }
}
