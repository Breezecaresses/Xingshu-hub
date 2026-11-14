import assert from 'node:assert/strict';
import {
  AppUpdateController,
  UPDATE_PREF_KEYS,
  type AppUpdateCheckResult,
  type UpdateStorageLike,
  type UpdateTimerApi,
} from '../shared/appUpdateCore.ts';

class MemoryStorage implements UpdateStorageLike {
  readonly data = new Map<string, string>();
  getItem(key: string): string | null {
    return this.data.has(key) ? (this.data.get(key) ?? null) : null;
  }
  setItem(key: string, value: string): void {
    this.data.set(key, value);
  }
}

class FakeTimers implements UpdateTimerApi {
  private nextId = 1;
  readonly timeouts = new Map<number, { fn: () => void; ms: number }>();
  readonly intervals = new Map<number, { fn: () => void; ms: number }>();

  setTimeout(fn: () => void, ms: number): number {
    const id = this.nextId++;
    this.timeouts.set(id, { fn, ms });
    return id;
  }
  clearTimeout(id: unknown): void {
    this.timeouts.delete(Number(id));
  }
  setInterval(fn: () => void, ms: number): number {
    const id = this.nextId++;
    this.intervals.set(id, { fn, ms });
    return id;
  }
  clearInterval(id: unknown): void {
    this.intervals.delete(Number(id));
  }
  fireTimeouts(): void {
    const entries = [...this.timeouts.entries()];
    this.timeouts.clear();
    for (const [, entry] of entries) entry.fn();
  }
  fireIntervals(): void {
    for (const entry of [...this.intervals.values()]) entry.fn();
  }
}

async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 8; i++) await Promise.resolve();
}

function makeReleaseResult(sequence: number, canApply = true): AppUpdateCheckResult {
  return {
    available: true,
    currentVersion: '0.1.4',
    canApply,
    release: {
      version: `0.1.${sequence}`,
      sequence,
      notes: 'fix & polish',
      desktop: { size: 10_485_760 },
      android: { versionCode: sequence },
    },
    releaseUrl: 'https://github.com/example/releases',
    downloadSource: 'GitHub',
    message: `发现新版本 0.1.${sequence}`,
  };
}

async function runTests(): Promise<void> {
  // 1. Default preferences and persistence across restarts.
  const storage = new MemoryStorage();
  const timers = new FakeTimers();
  const calls: Array<{ command: string; args?: Record<string, unknown> }> = [];
  let checkResponse: AppUpdateCheckResult = {
    available: false,
    currentVersion: '0.1.4',
    canApply: true,
    release: null,
    releaseUrl: 'https://github.com/example/releases',
    message: '已是最新版本',
  };

  const controller = new AppUpdateController({
    storage,
    timers,
    now: () => 1_700_000_000_000,
    isDesktopRuntime: () => true,
    invoke: async <T>(command: string, args?: Record<string, unknown>): Promise<T> => {
      calls.push({ command, args });
      if (command === 'get_app_build') return { version: '0.1.4' } as T;
      if (command === 'check_app_update') return checkResponse as T;
      if (command === 'prepare_app_update') {
        return makeReleaseResult(Number(args?.sequence ?? 5)) as T;
      }
      throw new Error(`unexpected command ${command}`);
    },
  });

  assert.deepEqual(controller.getSnapshot().prefs, {
    autoCheck: true,
    includePreview: true,
    autoDownload: false,
  });

  const stop = controller.startBackgroundSync();
  await flushMicrotasks();
  assert.equal(controller.getSnapshot().buildVersion, '0.1.4');
  assert.equal(timers.timeouts.size, 1);
  assert.equal(timers.intervals.size, 1);

  // Trigger startup check -> finds v0.1.5 -> opens modal once for seq 5.
  checkResponse = makeReleaseResult(5, true);
  timers.fireTimeouts();
  await flushMicrotasks();
  assert.equal(controller.getSnapshot().modalOpen, true);
  assert.equal(controller.getSnapshot().autoPromptedSequence, 5);
  assert.equal(controller.getSnapshot().phase, 'idle');
  assert.equal(calls.find(c => c.command === 'check_app_update')?.args?.preview, true);

  // Dismiss modal and simulate 30-min periodic tick for same sequence -> does not reopen modal.
  controller.closeModal();
  assert.equal(controller.getSnapshot().modalOpen, false);
  timers.fireIntervals();
  await flushMicrotasks();
  assert.equal(controller.getSnapshot().modalOpen, false);

  // 2. Enabling autoDownload while applicable release is present downloads immediately without restarting.
  controller.setPreference('autoDownload', true);
  await flushMicrotasks();
  assert.equal(storage.getItem(UPDATE_PREF_KEYS.autoDownload), 'true');
  assert.equal(controller.getSnapshot().phase, 'ready');
  assert.equal(controller.getSnapshot().preparedSequence, 5);
  assert.equal(calls.some(c => c.command === 'apply_app_update'), false);

  // 3. Switching preview preference resets stale ready state and re-checks with preview=false.
  controller.setPreference('includePreview', false);
  assert.equal(storage.getItem(UPDATE_PREF_KEYS.includePreview), 'false');
  await flushMicrotasks();
  const checkCalls = calls.filter(c => c.command === 'check_app_update');
  assert.equal(checkCalls[checkCalls.length - 1]?.args?.preview, false);

  // 4. Disabling autoCheck stops background timers and does not check automatically.
  const countBeforeDisable = calls.filter(c => c.command === 'check_app_update').length;
  controller.setPreference('autoCheck', false);
  assert.equal(storage.getItem(UPDATE_PREF_KEYS.autoCheck), 'false');
  assert.equal(timers.timeouts.size, 0);
  assert.equal(timers.intervals.size, 0);
  assert.equal(calls.filter(c => c.command === 'check_app_update').length, countBeforeDisable);

  // Reloading a second controller inherits saved preferences and stays quiet on startup.
  const timers2 = new FakeTimers();
  const calls2: string[] = [];
  const reloaded = new AppUpdateController({
    storage,
    timers: timers2,
    isDesktopRuntime: () => true,
    invoke: async <T>(command: string): Promise<T> => {
      calls2.push(command);
      return (command === 'get_app_build' ? { version: '0.1.4' } : checkResponse) as T;
    },
  });
  assert.deepEqual(reloaded.getSnapshot().prefs, {
    autoCheck: false,
    includePreview: false,
    autoDownload: true,
  });
  const stop2 = reloaded.startBackgroundSync();
  await flushMicrotasks();
  assert.equal(timers2.timeouts.size, 0);
  assert.equal(timers2.intervals.size, 0);
  assert.equal(calls2.includes('check_app_update'), false);

  // Re-enabling autoCheck restarts periodic sync and runs an immediate check.
  reloaded.setPreference('autoCheck', true);
  await flushMicrotasks();
  assert.equal(timers2.intervals.size, 1);
  assert.equal(calls2.includes('check_app_update'), true);

  // 5. Silent network errors stay out of the blocking error banner, while manual check reports them.
  const errController = new AppUpdateController({
    storage: new MemoryStorage(),
    timers: new FakeTimers(),
    isDesktopRuntime: () => true,
    invoke: async () => {
      throw new Error('network timeout');
    },
  });
  await errController.checkForUpdates({ silent: true });
  assert.equal(errController.getSnapshot().error, '');
  assert.equal(errController.getSnapshot().lastAutoError, 'network timeout');
  await errController.checkForUpdates({ silent: false });
  assert.equal(errController.getSnapshot().error, 'network timeout');

  stop();
  stop2();
  console.log('PASS: desktop auto-update preferences, scheduling, auto-download, and error isolation verified');
}

void runTests();
