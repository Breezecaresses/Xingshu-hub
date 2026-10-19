/**
 * Project XingShu — HS4-P1.2 Managed Spectator Account Provider
 * Service implementation for cloud-managed spectator account orchestration.
 *
 * Current Phase Status:
 * The official cloud watcher pool service is NOT yet implemented in production.
 * This provider honestly reports SERVICE_NOT_CONFIGURED by default, guiding users
 * to use "我的观战账号" (local client spectator account) via ADR-007 architecture.
 *
 * Security Mandate:
 * Zero hardcoded credentials or plain text passwords exist anywhere in this provider.
 * When connected to a future remote watcher pool server, passwords exist exclusively
 * on the remote headless worker cluster and are never exposed to the frontend.
 */

import {
  type ManagedProviderStatus,
  ManagedProviderStatus as ProviderStatus,
  type ManagedAccountInfo,
  type IManagedSpectatorAccountProvider,
  type ManagedSessionAllocateResponse,
  type ManagedServerStatusResponse,
  MANAGED_SPECTATOR_API,
  OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE,
  OFFICIAL_WATCHER_UNCONFIGURED_ERROR,
} from '../types/spectatorAccount.ts';

// Re-export required types
export type {
  ManagedProviderStatus,
  ManagedAccountInfo,
  IManagedSpectatorAccountProvider,
};

/**
 * Configuration options for DefaultManagedSpectatorAccountProvider.
 */
export interface ManagedAccountProviderOptions {
  /**
   * Explicit base URL of the remote watcher pool server (e.g. "https://spectator.xingshu.internal").
   * Overrides environment variables if specified.
   */
  serverEndpoint?: string | null;

  /**
   * Custom fetch implementation for unit tests or network proxies. Defaults to global fetch.
   */
  fetchFn?: typeof fetch;

  /**
   * Network request timeout in milliseconds. Defaults to 10000ms.
   */
  timeoutMs?: number;
}

/**
 * Default implementation of the Managed Spectator Account Provider.
 */
export class DefaultManagedSpectatorAccountProvider
  implements IManagedSpectatorAccountProvider
{
  readonly executionMode = 'REMOTE' as const;
  private customEndpoint: string | null | undefined;
  private readonly fetchFn: typeof fetch;
  private readonly timeoutMs: number;

  constructor(options?: ManagedAccountProviderOptions) {
    if (options?.serverEndpoint !== undefined) {
      this.customEndpoint = this.sanitizeEndpoint(options.serverEndpoint);
    }
    this.fetchFn =
      options?.fetchFn ??
      (typeof fetch !== 'undefined' ? fetch.bind(globalThis) : (undefined as any));
    this.timeoutMs = options?.timeoutMs ?? 10000;
  }

  /**
   * Sanitizes an endpoint string by trimming and removing trailing slashes.
   */
  private sanitizeEndpoint(endpoint: string | null | undefined): string | null {
    if (!endpoint) return null;
    const trimmed = endpoint.trim();
    if (trimmed.length === 0) return null;
    const url = new URL(trimmed);
    const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
    if ((url.protocol !== 'https:' && !(loopback && url.protocol === 'http:'))
        || url.username || url.password || url.search || url.hash) {
      throw new Error('托管服务地址须为 HTTPS（本机可用 HTTP），且不得包含凭据、查询或片段');
    }
    return url.href.replace(/\/+$/, '');
  }

  /**
   * Updates or clears the server endpoint dynamically at runtime.
   */
  public setServerEndpoint(endpoint: string | null): void {
    this.customEndpoint = this.sanitizeEndpoint(endpoint);
  }

  private async readJson(response: Response, signal: AbortSignal): Promise<any> {
    if (signal.aborted) throw new Error('托管服务请求超时');
    let onAbort: () => void = () => {};
    const aborted = new Promise<never>((_, reject) => {
      onAbort = () => reject(new Error('托管服务请求超时'));
      signal.addEventListener('abort', onAbort, { once: true });
    });
    try { return await Promise.race([response.json(), aborted]); }
    finally { signal.removeEventListener('abort', onAbort); }
  }

  /**
   * Resolves the configured spectator server endpoint.
   * Priority:
   * 1. Explicitly configured customEndpoint (if non-null)
   * 2. process.env.XINGSHU_SPECTATOR_SERVER_URL (Node.js / Tauri backend)
   * 3. import.meta.env.VITE_XINGSHU_SPECTATOR_SERVER_URL / XINGSHU_SPECTATOR_SERVER_URL (Vite / Frontend)
   * Returns null if no valid endpoint is configured.
   */
  public getServerEndpoint(): string | null {
    if (this.customEndpoint !== undefined) {
      return this.customEndpoint;
    }

    // 1. Check Node.js / Tauri environment variables safely
    const globalProc = (globalThis as Record<string, unknown>).process as
      | { env?: Record<string, string | undefined> }
      | undefined;
    if (globalProc && typeof globalProc.env === 'object') {
      const nodeEnv = globalProc.env.XINGSHU_SPECTATOR_SERVER_URL;
      if (nodeEnv && typeof nodeEnv === 'string') {
        const sanitized = this.sanitizeEndpoint(nodeEnv);
        if (sanitized) return sanitized;
      }
    }

    // 2. Check Vite / Browser build environment variables
    try {
      const metaEnv = (import.meta as any)?.env;
      if (metaEnv) {
        const viteEnv =
          metaEnv.VITE_XINGSHU_SPECTATOR_SERVER_URL ||
          metaEnv.XINGSHU_SPECTATOR_SERVER_URL;
        if (viteEnv && typeof viteEnv === 'string') {
          const sanitized = this.sanitizeEndpoint(viteEnv);
          if (sanitized) return sanitized;
        }
      }
    } catch {
      // Safe fallback in environments where import.meta is restricted
    }

    return null;
  }

  /**
   * Queries provider status.
   * If not configured, returns honest SERVICE_NOT_CONFIGURED report.
   * If configured, contacts remote server status endpoint.
   */
  public async getStatus(): Promise<ManagedAccountInfo> {
    const endpoint = this.getServerEndpoint();

    if (!endpoint) {
      return {
        status: 'SERVICE_NOT_CONFIGURED',
        serverEndpoint: null,
        sessionId: null,
        allocatedPlayerId: null,
        allocatedAt: null,
        expiresAt: null,
        message: OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE,
      };
    }

    if (!this.fetchFn) {
      return {
        status: 'ERROR',
        serverEndpoint: endpoint,
        sessionId: null,
        allocatedPlayerId: null,
        allocatedAt: null,
        expiresAt: null,
        message: 'No fetch implementation available in current environment.',
      };
    }

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const url = `${endpoint}${MANAGED_SPECTATOR_API.STATUS}`;
      const response = await this.fetchFn(url, {
        method: 'GET',
        headers: {
          Accept: 'application/json',
        },
        signal: controller.signal,
      });

      if (!response.ok) {
        return {
          status: 'ERROR',
          serverEndpoint: endpoint,
          sessionId: null,
          allocatedPlayerId: null,
          allocatedAt: null,
          expiresAt: null,
          message: `Spectator pool server returned HTTP ${response.status}: ${response.statusText}`,
        };
      }

      const data = (await this.readJson(response, controller.signal)) as ManagedServerStatusResponse;
      if (!data || !Object.values(ProviderStatus).includes(data.status)) {
        throw new Error('托管服务缺少有效状态，无法确认服务可用');
      }
      if (data.status === 'AVAILABLE' && data.availableCapacity !== undefined
          && (!Number.isSafeInteger(data.availableCapacity) || data.availableCapacity <= 0)) {
        throw new Error('托管服务容量与可用状态不一致');
      }
      return {
        status: data.status,
        serverEndpoint: endpoint,
        sessionId: null,
        allocatedPlayerId: null,
        allocatedAt: null,
        expiresAt: null,
        message: data.message ?? 'Official spectator pool service online and available.',
      };
    } catch (err: any) {
      clearTimeout(timeoutId);
      return {
        status: 'ERROR',
        serverEndpoint: endpoint,
        sessionId: null,
        allocatedPlayerId: null,
        allocatedAt: null,
        expiresAt: null,
        message: `Failed to query spectator pool server: ${err?.message ?? String(err)}`,
      };
    } finally {
      clearTimeout(timeoutId);
    }
  }

  /**
   * Requests allocation of an ephemeral spectator session.
   * If unconfigured, rejects immediately with OFFICIAL_WATCHER_UNCONFIGURED_ERROR.
   * If configured, invokes HTTP POST /api/v1/spectator/session/allocate on remote pool.
   */
  public async allocateSession(roomId?: number, watchCode?: string): Promise<ManagedAccountInfo> {
    const endpoint = this.getServerEndpoint();

    if (!endpoint) {
      throw new Error(OFFICIAL_WATCHER_UNCONFIGURED_ERROR);
    }

    const code = watchCode?.trim();
    if (!Number.isSafeInteger(roomId) || (roomId ?? 0) <= 0 || !code || !/^[A-Za-z0-9]{1,64}$/.test(code)) {
      throw new Error('申请托管会话需要真实房间号和观战码');
    }

    if (!this.fetchFn) {
      throw new Error('No fetch implementation available in current environment.');
    }

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const url = `${endpoint}${MANAGED_SPECTATOR_API.ALLOCATE}`;
      const response = await this.fetchFn(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
        },
        body: JSON.stringify({ roomId, watchCode: code }),
        signal: controller.signal,
      });

      if (!response.ok) {
        let errorDetail = '';
        try {
          const errorJson = await this.readJson(response, controller.signal);
          if (errorJson?.message) {
            errorDetail = `: ${errorJson.message}`;
          }
        } catch {
          errorDetail = `: HTTP ${response.status} ${response.statusText}`;
        }
        throw new Error(`Spectator account allocation failed${errorDetail}`);
      }

      const data = (await this.readJson(response, controller.signal)) as ManagedSessionAllocateResponse;

      if (!data || !['ALLOCATING', 'READY', 'IN_USE'].includes(data.status)
          || typeof data.sessionId !== 'string' || !data.sessionId.trim()
          || !Number.isSafeInteger(data.allocatedPlayerId) || data.allocatedPlayerId <= 0
          || !Number.isFinite(Date.parse(data.allocatedAt)) || !Number.isFinite(Date.parse(data.expiresAt))
          || Date.parse(data.expiresAt) <= Date.now() || Date.parse(data.allocatedAt) >= Date.parse(data.expiresAt)) {
        throw new Error('托管服务未返回有效且未过期的账号租约');
      }
      return {
        status: data.status,
        executionMode: 'REMOTE',
        currentRoomId: roomId,
        serverEndpoint: endpoint,
        sessionId: data.sessionId,
        allocatedPlayerId: data.allocatedPlayerId,
        allocatedAt: data.allocatedAt,
        expiresAt: data.expiresAt,
        message: data.message ?? 'Official spectator session allocated successfully.',
      };
    } catch (err: any) {
      clearTimeout(timeoutId);
      if (err.message === OFFICIAL_WATCHER_UNCONFIGURED_ERROR) {
        throw err;
      }
      throw new Error(`Spectator session allocation failed: ${err?.message ?? String(err)}`);
    } finally {
      clearTimeout(timeoutId);
    }
  }

  /**
   * Releases an active spectator session back to the pool.
   * If unconfigured, rejects with OFFICIAL_WATCHER_UNCONFIGURED_ERROR.
   */
  public async releaseSession(sessionId: string): Promise<void> {
    const endpoint = this.getServerEndpoint();

    if (!endpoint) {
      throw new Error(OFFICIAL_WATCHER_UNCONFIGURED_ERROR);
    }

    if (!sessionId || typeof sessionId !== 'string' || sessionId.trim().length === 0) {
      throw new Error('Invalid sessionId: must be a non-empty string.');
    }

    if (!this.fetchFn) {
      throw new Error('No fetch implementation available in current environment.');
    }

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const url = `${endpoint}${MANAGED_SPECTATOR_API.RELEASE}`;
      const response = await this.fetchFn(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
        },
        body: JSON.stringify({ sessionId: sessionId.trim() }),
        signal: controller.signal,
      });

      if (!response.ok) {
        let errorDetail = '';
        try {
          const errorJson = await this.readJson(response, controller.signal);
          if (errorJson?.message) {
            errorDetail = `: ${errorJson.message}`;
          }
        } catch {
          errorDetail = `: HTTP ${response.status} ${response.statusText}`;
        }
        throw new Error(`Spectator session release failed${errorDetail}`);
      }
      const result = await this.readJson(response, controller.signal);
      if (result?.success !== true) throw new Error('托管服务未确认租约释放');
    } catch (err: any) {
      clearTimeout(timeoutId);
      if (err.message === OFFICIAL_WATCHER_UNCONFIGURED_ERROR) {
        throw err;
      }
      throw new Error(`Spectator session release failed: ${err?.message ?? String(err)}`);
    } finally {
      clearTimeout(timeoutId);
    }
  }
}

/**
 * Singleton instance of DefaultManagedSpectatorAccountProvider.
 */
export const defaultManagedSpectatorAccountProvider =
  new DefaultManagedSpectatorAccountProvider();
