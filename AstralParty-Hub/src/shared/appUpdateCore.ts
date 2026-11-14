export type AppUpdateRelease = {
  version: string;
  sequence: number;
  notes: string;
  desktop: { size: number };
  android: { versionCode: number };
};

export type AppUpdateCheckResult = {
  available: boolean;
  currentVersion: string;
  canApply: boolean;
  release: AppUpdateRelease | null;
  releaseUrl: string;
  downloadSource?: string;
  checkWarning?: string;
  message: string;
};

export type AppUpdatePreferences = {
  autoCheck: boolean;
  includePreview: boolean;
  autoDownload: boolean;
};

export type AppUpdatePhase = 'idle' | 'checking' | 'downloading' | 'ready' | 'applying';

export type AppUpdateState = {
  isDesktopRuntime: boolean;
  buildVersion: string | null;
  buildLoadFailed: boolean;
  prefs: AppUpdatePreferences;
  phase: AppUpdatePhase;
  result: AppUpdateCheckResult | null;
  preparedSequence: number | null;
  lastCheckedAt: number | null;
  lastCheckOrigin: 'auto' | 'manual' | null;
  lastAutoError: string;
  error: string;
  bannerDismissedSequence: number | null;
  modalOpen: boolean;
  autoPromptedSequence: number | null;
};

export interface UpdateStorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

export interface UpdateTimerApi {
  setTimeout(fn: () => void, ms: number): unknown;
  clearTimeout(id: unknown): void;
  setInterval(fn: () => void, ms: number): unknown;
  clearInterval(id: unknown): void;
}

export type UpdateInvokeFn = <T = unknown>(command: string, args?: Record<string, unknown>) => Promise<T>;

export type AppUpdateControllerDeps = {
  invoke: UpdateInvokeFn;
  isDesktopRuntime: () => boolean;
  storage?: UpdateStorageLike | null;
  timers?: UpdateTimerApi;
  now?: () => number;
  startupDelayMs?: number;
  intervalMs?: number;
};

export const UPDATE_PREF_KEYS = {
  autoCheck: 'xingshu.update.autoCheck',
  includePreview: 'xingshu.update.includePreview',
  autoDownload: 'xingshu.update.autoDownload',
} as const;

export const DEFAULT_STARTUP_DELAY_MS = 1100;
export const DEFAULT_CHECK_INTERVAL_MS = 30 * 60 * 1000;

function readBoolPref(storage: UpdateStorageLike | null | undefined, key: string, fallback: boolean): boolean {
  if (!storage) return fallback;
  try {
    const raw = storage.getItem(key);
    if (raw === 'true' || raw === '1') return true;
    if (raw === 'false' || raw === '0') return false;
  } catch {
    // Ignore storage read errors in restricted WebViews.
  }
  return fallback;
}

function writeBoolPref(storage: UpdateStorageLike | null | undefined, key: string, value: boolean): void {
  if (!storage) return;
  try {
    storage.setItem(key, value ? 'true' : 'false');
  } catch {
    // Ignore storage write errors in restricted WebViews.
  }
}

export function extractErrorMessage(err: unknown): string {
  if (err instanceof Error) return err.message;
  if (typeof err === 'object' && err !== null && 'message' in err) {
    return String((err as { message?: unknown }).message);
  }
  return String(err);
}

export function loadUpdatePreferences(storage?: UpdateStorageLike | null): AppUpdatePreferences {
  return {
    autoCheck: readBoolPref(storage, UPDATE_PREF_KEYS.autoCheck, true),
    includePreview: readBoolPref(storage, UPDATE_PREF_KEYS.includePreview, true),
    autoDownload: readBoolPref(storage, UPDATE_PREF_KEYS.autoDownload, false),
  };
}

function canAutoPrepare(
  prefs: AppUpdatePreferences,
  result: AppUpdateCheckResult | null,
  preparedSequence: number | null,
): result is AppUpdateCheckResult & { release: AppUpdateRelease } {
  return Boolean(
    prefs.autoDownload
    && result?.available
    && result.release
    && result.canApply
    && !result.checkWarning
    && preparedSequence !== result.release.sequence,
  );
}

export class AppUpdateController {
  private readonly invoke: UpdateInvokeFn;
  private readonly detectDesktop: () => boolean;
  private readonly storage: UpdateStorageLike | null;
  private readonly timers: UpdateTimerApi;
  private readonly now: () => number;
  private readonly startupDelayMs: number;
  private readonly intervalMs: number;

  private state: AppUpdateState;
  private readonly listeners = new Set<() => void>();
  private inFlight: Promise<void> | null = null;
  private buildLoaded = false;
  private schedulerRefCount = 0;
  private startupTimer: unknown = null;
  private periodicTimer: unknown = null;

  constructor(deps: AppUpdateControllerDeps) {
    this.invoke = deps.invoke;
    this.detectDesktop = deps.isDesktopRuntime;
    this.storage = deps.storage ?? null;
    this.timers = deps.timers ?? {
      setTimeout: (fn, ms) => globalThis.setTimeout(fn, ms),
      clearTimeout: (id) => globalThis.clearTimeout(id as ReturnType<typeof setTimeout>),
      setInterval: (fn, ms) => globalThis.setInterval(fn, ms),
      clearInterval: (id) => globalThis.clearInterval(id as ReturnType<typeof setInterval>),
    };
    this.now = deps.now ?? (() => Date.now());
    this.startupDelayMs = deps.startupDelayMs ?? DEFAULT_STARTUP_DELAY_MS;
    this.intervalMs = deps.intervalMs ?? DEFAULT_CHECK_INTERVAL_MS;
    this.state = {
      isDesktopRuntime: this.detectDesktop(),
      buildVersion: null,
      buildLoadFailed: false,
      prefs: loadUpdatePreferences(this.storage),
      phase: 'idle',
      result: null,
      preparedSequence: null,
      lastCheckedAt: null,
      lastCheckOrigin: null,
      lastAutoError: '',
      error: '',
      bannerDismissedSequence: null,
      modalOpen: false,
      autoPromptedSequence: null,
    };
  }

  getSnapshot = (): AppUpdateState => this.state;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  private setState(patch: Partial<AppUpdateState>): void {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) {
      listener();
    }
  }

  async ensureBuildLoaded(): Promise<void> {
    const desktop = this.detectDesktop();
    if (desktop !== this.state.isDesktopRuntime) {
      this.setState({ isDesktopRuntime: desktop });
    }
    if (!desktop || this.buildLoaded) return;
    this.buildLoaded = true;
    try {
      const build = await this.invoke<{ version?: string }>('get_app_build');
      if (build && typeof build.version === 'string' && build.version.trim()) {
        this.setState({ buildVersion: build.version, buildLoadFailed: false });
      } else {
        this.setState({ buildLoadFailed: true });
      }
    } catch {
      this.setState({ buildLoadFailed: true });
    }
  }

  startBackgroundSync(): () => void {
    this.schedulerRefCount++;
    if (this.schedulerRefCount === 1) {
      void this.ensureBuildLoaded();
      this.rescheduleTimers(true);
    }
    return () => {
      this.schedulerRefCount = Math.max(0, this.schedulerRefCount - 1);
      if (this.schedulerRefCount === 0) {
        this.clearTimers();
      }
    };
  }

  private clearTimers(): void {
    if (this.startupTimer !== null) {
      this.timers.clearTimeout(this.startupTimer);
      this.startupTimer = null;
    }
    if (this.periodicTimer !== null) {
      this.timers.clearInterval(this.periodicTimer);
      this.periodicTimer = null;
    }
  }

  private rescheduleTimers(scheduleStartup: boolean): void {
    this.clearTimers();
    if (!this.state.prefs.autoCheck || !this.detectDesktop() || this.schedulerRefCount <= 0) {
      return;
    }
    if (scheduleStartup) {
      this.startupTimer = this.timers.setTimeout(() => {
        this.startupTimer = null;
        if (this.state.prefs.autoCheck) {
          void this.checkForUpdates({ silent: true });
        }
      }, this.startupDelayMs);
    }
    if (this.intervalMs > 0) {
      this.periodicTimer = this.timers.setInterval(() => {
        if (this.state.prefs.autoCheck) {
          void this.checkForUpdates({ silent: true });
        }
      }, this.intervalMs);
    }
  }

  setPreference<K extends keyof AppUpdatePreferences>(key: K, value: AppUpdatePreferences[K]): void {
    if (this.state.prefs[key] === value) return;
    const nextPrefs: AppUpdatePreferences = { ...this.state.prefs, [key]: value };
    writeBoolPref(this.storage, UPDATE_PREF_KEYS[key], value);

    if (key === 'includePreview') {
      const resetPhase = this.state.phase === 'ready' ? 'idle' : this.state.phase;
      this.setState({
        prefs: nextPrefs,
        phase: resetPhase,
        result: null,
        preparedSequence: null,
        error: '',
        lastAutoError: '',
      });
      if (nextPrefs.autoCheck && this.detectDesktop()) {
        void this.checkForUpdates({ silent: true });
      }
      return;
    }

    if (key === 'autoCheck') {
      this.setState({
        prefs: nextPrefs,
        lastAutoError: '',
      });
      this.rescheduleTimers(false);
      if (value && this.detectDesktop()) {
        void this.checkForUpdates({ silent: true });
      }
      return;
    }

    this.setState({ prefs: nextPrefs });
    if (value && this.detectDesktop() && canAutoPrepare(nextPrefs, this.state.result, this.state.preparedSequence)) {
      void this.prepareUpdate({ silent: true });
    }
  }

  openModal = (): void => {
    this.setState({ modalOpen: true });
  };

  closeModal = (): void => {
    if (this.state.phase === 'downloading' || this.state.phase === 'applying') return;
    this.setState({ modalOpen: false });
  };

  dismissBanner = (): void => {
    if (this.state.phase === 'downloading' || this.state.phase === 'applying') return;
    const seq = this.state.result?.release?.sequence ?? this.state.bannerDismissedSequence;
    this.setState({ bannerDismissedSequence: seq });
  };

  async checkForUpdates(options?: { silent?: boolean }): Promise<void> {
    const silent = options?.silent ?? false;
    if (this.inFlight) {
      return silent ? undefined : this.inFlight;
    }
    if (!this.detectDesktop()) {
      if (!silent) {
        this.setState({ error: '当前处于浏览器网页预览模式，检查并安装 Hub 更新请在桌面端应用中使用。' });
      }
      return;
    }

    const previewSnapshot = this.state.prefs.includePreview;
    const run = (async () => {
      this.setState({
        phase: 'checking',
        error: silent ? this.state.error : '',
      });
      try {
        const next = await this.invoke<AppUpdateCheckResult>('check_app_update', { preview: previewSnapshot });
        const releaseSeq = next.available && next.release ? next.release.sequence : null;
        const alreadyPrepared = releaseSeq !== null && this.state.preparedSequence === releaseSeq;
        const shouldAutoOpenModal = Boolean(
          silent
          && next.available
          && next.release
          && this.state.autoPromptedSequence !== next.release.sequence,
        );
        this.setState({
          phase: alreadyPrepared ? 'ready' : 'idle',
          result: next,
          preparedSequence: alreadyPrepared ? this.state.preparedSequence : null,
          lastCheckedAt: this.now(),
          lastCheckOrigin: silent ? 'auto' : 'manual',
          lastAutoError: '',
          error: '',
          autoPromptedSequence: shouldAutoOpenModal && next.release
            ? next.release.sequence
            : this.state.autoPromptedSequence,
          modalOpen: shouldAutoOpenModal ? true : this.state.modalOpen,
        });

        if (canAutoPrepare(this.state.prefs, next, this.state.preparedSequence)) {
          await this.prepareInternal(next.release.sequence, previewSnapshot, { silent });
        }
      } catch (err) {
        const msg = extractErrorMessage(err);
        if (silent) {
          this.setState({
            phase: this.state.preparedSequence !== null ? 'ready' : 'idle',
            lastCheckOrigin: 'auto',
            lastAutoError: msg,
          });
        } else {
          this.setState({
            phase: this.state.preparedSequence !== null ? 'ready' : 'idle',
            lastCheckOrigin: 'manual',
            error: msg,
          });
        }
      } finally {
        this.inFlight = null;
      }
    })();

    this.inFlight = run;
    return run;
  }

  private async prepareInternal(
    sequence: number,
    preview: boolean,
    options?: { silent?: boolean },
  ): Promise<void> {
    const silent = options?.silent ?? false;
    this.setState({
      phase: 'downloading',
      error: silent ? this.state.error : '',
    });
    try {
      const next = await this.invoke<AppUpdateCheckResult>('prepare_app_update', {
        preview,
        sequence,
      });
      const preparedSeq = next.release?.sequence ?? sequence;
      this.setState({
        phase: 'ready',
        result: next,
        preparedSequence: preparedSeq,
        error: '',
        lastAutoError: '',
      });
    } catch (err) {
      const msg = extractErrorMessage(err);
      this.setState({
        phase: 'idle',
        preparedSequence: null,
        ...(silent ? { lastAutoError: msg } : { error: msg }),
      });
    }
  }

  async prepareUpdate(options?: { silent?: boolean }): Promise<void> {
    const silent = options?.silent ?? false;
    if (this.inFlight) return silent ? undefined : this.inFlight;
    const sequence = this.state.result?.release?.sequence;
    if (sequence === undefined) return;
    if (!this.detectDesktop()) {
      if (!silent) {
        this.setState({ error: '当前处于浏览器网页预览模式，下载与安装更新请在桌面端应用中使用。' });
      }
      return;
    }

    const previewSnapshot = this.state.prefs.includePreview;
    const run = this.prepareInternal(sequence, previewSnapshot, { silent }).finally(() => {
      this.inFlight = null;
    });
    this.inFlight = run;
    return run;
  }

  async applyUpdate(): Promise<void> {
    if (this.inFlight) return this.inFlight;
    if (!this.detectDesktop()) {
      this.setState({ error: '当前处于浏览器网页预览模式，重启更新请在桌面端应用中使用。' });
      return;
    }

    const run = (async () => {
      this.setState({ phase: 'applying', error: '' });
      try {
        await this.invoke('apply_app_update');
      } catch (err) {
        const msg = extractErrorMessage(err);
        this.setState({
          phase: this.state.preparedSequence !== null ? 'ready' : 'idle',
          error: msg,
        });
      } finally {
        this.inFlight = null;
      }
    })();

    this.inFlight = run;
    return run;
  }
}
