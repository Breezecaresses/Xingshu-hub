/**
 * Project XingShu — User-Owned Spectator Account Service
 * Phase: HS4-P1.2
 *
 * Implements account metadata persistence, state derivation, runtime synchronization,
 * and launcher URI triggers for user-owned spectator accounts.
 *
 * STRICT SECURITY INVARIANT:
 * Prohibits storing passwords, tokens, auth keys, cookies, or secrets.
 * Only safe metadata (platformId, playerId, displayName, lastSeenAt) is persisted.
 */

import {
  SpectatorAccountStatus,
  type UserOwnedAccountInfo,
  type UserOwnedAccountMetadata,
} from '../types/spectatorAccount.ts';
import { resolvePlatformLaunchStrategy } from './platformLaunchStrategy.ts';

export type { UserOwnedAccountInfo, UserOwnedAccountMetadata };

export const USER_SPECTATOR_ACCOUNT_STORAGE_KEY = 'xingshu_user_spectator_account';

/**
 * Canonical Steam Application ID for Astral Party.
 * Verified Single Source of Truth (SSOT).
 */
export const CANONICAL_ASTRAL_PARTY_STEAM_APP_ID = 2622000;

/**
 * Generates canonical steam:// launch URI for the given Steam App ID.
 */
export function getSteamLaunchUri(appId: number = CANONICAL_ASTRAL_PARTY_STEAM_APP_ID): string {
  return `steam://run/${appId}`;
}

/**
 * List of forbidden key fragments to prevent credential leakage.
 */
export const FORBIDDEN_SECURITY_KEYS: readonly string[] = [
  'password',
  'passwd',
  'pwd',
  'secret',
  'token',
  'auth',
  'cookie',
  'credential',
  'apikey',
  'private',
  'bearer',
  'session',
] as const;

/**
 * Platform display name mappings.
 */
export const PLATFORM_NAMES: Record<string, string> = {
  STEAM_CN: 'Steam 国服',
  STEAM_GLOBAL: 'Steam 国际服',
  TAPTAP: 'TapTap 客户端',
  BILIBILI: 'Bilibili 客户端',
  WEGAME: 'WeGame 客户端',
};

/**
 * Minimal storage abstraction for webview / browser / node runner environments.
 */
export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

/**
 * In-memory fallback storage when window.localStorage is not available (e.g. Node tests).
 */
export class MemoryStorage implements StorageLike {
  private map = new Map<string, string>();

  getItem(key: string): string | null {
    return this.map.has(key) ? this.map.get(key)! : null;
  }

  setItem(key: string, value: string): void {
    this.map.set(key, String(value));
  }

  removeItem(key: string): void {
    this.map.delete(key);
  }

  clear(): void {
    this.map.clear();
  }
}

/**
 * Parameters for deriving user account status.
 */
export interface AccountStatusDerivationInput {
  gameRunning: boolean;
  runtimeConnected: boolean;
  spectatorAccountPlayerId?: number | null;
  roomId?: number | null;
  gameLoginState?: 'LOGGED_IN' | 'LOGGED_OUT' | null;
}

/**
 * Parameters for synchronizing user account information.
 */
export interface AccountSyncInput extends AccountStatusDerivationInput {
  platformId?: string;
  platformName?: string;
  displayName?: string | null;
}

/**
 * Service managing User-Owned Spectator Account state and zero-credential storage.
 */
export class UserOwnedSpectatorAccountService {
  private storage: StorageLike;
  private currentAccount: UserOwnedAccountInfo;

  constructor(customStorage?: StorageLike) {
    if (customStorage) {
      this.storage = customStorage;
    } else if (typeof window !== 'undefined' && window.localStorage) {
      this.storage = window.localStorage;
    } else {
      this.storage = new MemoryStorage();
    }

    const saved = this.loadSavedMetadata();
    const platformId = saved?.platformId || 'TAPTAP';
    const lastKnownPlayerId = saved?.lastKnownPlayerId ?? saved?.playerId ?? null;

    this.currentAccount = {
      platformId,
      platformName: PLATFORM_NAMES[platformId] || 'TapTap 客户端',
      currentSessionPlayerId: null, // STRICTLY NULL initially!
      playerId: null,              // STRICTLY NULL initially!
      lastKnownPlayerId,
      displayName: saved?.displayName ?? null,
      lastSeenAt: saved?.lastSeenAt ?? null,
      gameRunning: false,
      runtimeConnected: false,
      status: SpectatorAccountStatus.OFFLINE,
    };
  }

  /**
   * Retrieves the storage key used for saving metadata.
   */
  public getStorageKey(): string {
    return USER_SPECTATOR_ACCOUNT_STORAGE_KEY;
  }

  /**
   * Loads safe saved metadata from storage.
   */
  public loadSavedMetadata(): UserOwnedAccountMetadata | null {
    try {
      const raw = this.storage.getItem(USER_SPECTATOR_ACCOUNT_STORAGE_KEY);
      if (!raw) return null;

      const parsed = JSON.parse(raw);
      if (typeof parsed !== 'object' || parsed === null) {
        return null;
      }

      // Security check: verify no forbidden keys exist in saved storage
      UserOwnedSpectatorAccountService.validateSecurityGuarantee(parsed);

      const lastKnownPlayerId =
        typeof parsed.lastKnownPlayerId === 'number' && parsed.lastKnownPlayerId > 0
          ? parsed.lastKnownPlayerId
          : typeof parsed.playerId === 'number' && parsed.playerId > 0
          ? parsed.playerId
          : null;

      const platformId = typeof parsed.platformId === 'string' ? parsed.platformId : 'TAPTAP';

      return {
        platformId,
        lastKnownPlayerId,
        playerId: lastKnownPlayerId,
        displayName: typeof parsed.displayName === 'string' ? parsed.displayName : null,
        lastSeenAt: typeof parsed.lastSeenAt === 'string' ? parsed.lastSeenAt : null,
      };
    } catch (err) {
      console.warn('[SpectatorAccountService] Failed to load saved metadata:', err);
      return null;
    }
  }

  /**
   * Persists safe metadata into storage with strict whitelist filtering and credential rejection.
   */
  public saveMetadata(data: Partial<UserOwnedAccountMetadata> & Record<string, unknown>): UserOwnedAccountMetadata {
    // 1. Strict security audit: reject any payload containing forbidden credential keys
    const securityAudit = UserOwnedSpectatorAccountService.validateSecurityGuarantee(data);
    if (!securityAudit.isSafe) {
      throw new Error(
        `[SECURITY_VIOLATION] Attempted to store unauthorized credential fields: ${securityAudit.violations.join(', ')}`
      );
    }

    const lastKnownPlayerId =
      typeof data.lastKnownPlayerId === 'number' && data.lastKnownPlayerId > 0
        ? data.lastKnownPlayerId
        : typeof data.playerId === 'number' && data.playerId > 0
        ? data.playerId
        : this.currentAccount.lastKnownPlayerId;

    // 2. Strict whitelist filtering: ONLY safe fields are allowed into persistence
    const safePayload: UserOwnedAccountMetadata = {
      platformId: typeof data.platformId === 'string' && data.platformId.trim() ? data.platformId.trim() : (this.currentAccount.platformId || 'STEAM_CN'),
      lastKnownPlayerId,
      playerId: lastKnownPlayerId,
      displayName: typeof data.displayName === 'string' ? data.displayName.trim() : (this.currentAccount.displayName ?? null),
      lastSeenAt: typeof data.lastSeenAt === 'string' ? data.lastSeenAt : (new Date().toISOString()),
    };

    const json = JSON.stringify(safePayload);

    // Double-check serialized JSON output
    for (const forbidden of FORBIDDEN_SECURITY_KEYS) {
      const regex = new RegExp(`"${forbidden}"`, 'i');
      if (regex.test(json)) {
        throw new Error(`[SECURITY_VIOLATION] Serialized payload contains forbidden keyword "${forbidden}"`);
      }
    }

    this.storage.setItem(USER_SPECTATOR_ACCOUNT_STORAGE_KEY, json);

    // Update in-memory account representation
    this.currentAccount.platformId = safePayload.platformId;
    this.currentAccount.platformName = PLATFORM_NAMES[safePayload.platformId] || safePayload.platformId;
    this.currentAccount.lastKnownPlayerId = safePayload.lastKnownPlayerId;
    // NOTE: saveMetadata NEVER updates currentSessionPlayerId! currentSessionPlayerId is only updated via syncAccountInfo from live runtime!
    this.currentAccount.displayName = safePayload.displayName;
    this.currentAccount.lastSeenAt = safePayload.lastSeenAt;

    return safePayload;
  }

  /**
   * Clears saved metadata from storage and resets account state.
   */
  public clearSavedMetadata(): void {
    this.storage.removeItem(USER_SPECTATOR_ACCOUNT_STORAGE_KEY);
    this.currentAccount.currentSessionPlayerId = null;
    this.currentAccount.playerId = null;
    this.currentAccount.lastKnownPlayerId = null;
    this.currentAccount.displayName = null;
    this.currentAccount.lastSeenAt = null;
  }

  /**
   * Resets active session identity state when runtime disconnects or on session boundary.
   * Preserves platform preferences and lastKnownPlayerId in persistence.
   */
  public resetSession(gameRunning: boolean = false): void {
    this.currentAccount.currentSessionPlayerId = null;
    this.currentAccount.playerId = null;
    this.currentAccount.runtimeConnected = false;
    this.currentAccount.gameRunning = gameRunning;
    this.currentAccount.status = this.currentAccount.gameRunning
      ? SpectatorAccountStatus.WAITING_RUNTIME
      : SpectatorAccountStatus.OFFLINE;
  }

  /**
   * Derives operational status based on runtime signals.
   * Logic:
   * 1. If game not running: 'OFFLINE'
   * 2. If explicit logged out signal received: 'LOGIN_REQUIRED'
   * 3. If game running but runtime not connected: 'WAITING_RUNTIME'
   * 4. If runtime connected & roomId active (roomId != null && roomId > 0): 'IN_USE'
   *    (In IN_USE mode, even if playerId is temporarily null on the very first frame, status remains IN_USE)
   * 5. If runtime connected & spectator player ID known: 'READY'
   * 6. Otherwise (runtime connected, playerId null, roomId null): 'IDENTITY_PENDING'
   *
   * STRICT PROHIBITION:
   * Prohibit LOGIN_REQUIRED when runtime is connected!
   */
  public deriveStatus(input: AccountStatusDerivationInput): SpectatorAccountStatus {
    if (!input.gameRunning) {
      return SpectatorAccountStatus.OFFLINE;
    }

    if (input.gameLoginState === 'LOGGED_OUT') {
      return SpectatorAccountStatus.LOGIN_REQUIRED;
    }

    if (!input.runtimeConnected) {
      return SpectatorAccountStatus.WAITING_RUNTIME;
    }

    // In active match (even if playerId is temporarily null on the very first frame, status remains IN_USE)
    if (input.roomId != null && input.roomId > 0) {
      return SpectatorAccountStatus.IN_USE;
    }

    const effectivePlayerId =
      input.spectatorAccountPlayerId != null && input.spectatorAccountPlayerId > 0
        ? input.spectatorAccountPlayerId
        : this.currentAccount.currentSessionPlayerId;

    // Runtime connected and spectator player ID received / recognized
    if (effectivePlayerId != null && effectivePlayerId > 0) {
      return SpectatorAccountStatus.READY;
    }

    // Game is running and runtime is connected, waiting for spectator account identity packet
    return SpectatorAccountStatus.IDENTITY_PENDING;
  }

  /**
   * Synchronizes runtime state, recalculates status, updates lastSeenAt,
   * and saves updated safe metadata if new playerId is captured in the current session.
   *
   * CURRENT SESSION IDENTITY TRUTH:
   * - currentSessionPlayerId ONLY comes from live runtime spectatorAccountPlayerId.
   * - Disconnected -> currentSessionPlayerId = null
   * - Connected without packet -> currentSessionPlayerId = null (status = IDENTITY_PENDING)
   * - Live packet with ID -> currentSessionPlayerId = X, lastKnownPlayerId = X (status = READY or IN_USE)
   * - PROHIBITION: lastKnownPlayerId is NEVER automatically promoted to currentSessionPlayerId!
   */
  public syncAccountInfo(input: AccountSyncInput): UserOwnedAccountInfo {
    const platformId = input.platformId || this.currentAccount.platformId || 'STEAM_CN';
    const platformName = input.platformName || PLATFORM_NAMES[platformId] || this.currentAccount.platformName;

    let currentSessionPlayerId: number | null = null;
    let lastKnownPlayerId = this.currentAccount.lastKnownPlayerId;

    if (!input.runtimeConnected || !input.gameRunning) {
      // Rule 1: Runtime disconnected or game closed -> currentSessionPlayerId = null
      currentSessionPlayerId = null;
    } else {
      // Rule 2: Runtime connected -> update if genuine spectatorAccountPlayerId was emitted
      if (input.spectatorAccountPlayerId != null && input.spectatorAccountPlayerId > 0) {
        currentSessionPlayerId = input.spectatorAccountPlayerId;
        lastKnownPlayerId = input.spectatorAccountPlayerId; // Update persistent last-known on genuine receipt
      } else {
        // Retain existing session player ID if already captured in this active session
        currentSessionPlayerId = this.currentAccount.currentSessionPlayerId;
      }
    }

    const derivedStatus = this.deriveStatus({
      ...input,
      spectatorAccountPlayerId: currentSessionPlayerId,
    });

    let displayName = this.currentAccount.displayName;
    if (input.displayName !== undefined) {
      displayName = input.displayName;
    }

    const now = new Date().toISOString();
    let lastSeenAt = this.currentAccount.lastSeenAt;
    if (input.gameRunning || input.runtimeConnected) {
      lastSeenAt = now;
    }

    this.currentAccount = {
      platformId,
      platformName,
      currentSessionPlayerId,
      playerId: currentSessionPlayerId, // Strictly mirrors currentSessionPlayerId, NEVER elevated from lastKnownPlayerId!
      lastKnownPlayerId,
      displayName,
      lastSeenAt,
      gameRunning: input.gameRunning,
      runtimeConnected: input.runtimeConnected,
      status: derivedStatus,
    };

    // Auto-persist when genuine currentSessionPlayerId is identified
    if (currentSessionPlayerId != null && (input.gameRunning || input.runtimeConnected)) {
      try {
        this.saveMetadata({
          platformId,
          lastKnownPlayerId,
          playerId: lastKnownPlayerId,
          displayName,
          lastSeenAt,
        });
      } catch (e) {
        console.warn('[SpectatorAccountService] Auto-persist metadata warning:', e);
      }
    }

    return { ...this.currentAccount };
  }

  /**
   * Returns current account information snapshot.
   */
  public getAccountInfo(): UserOwnedAccountInfo {
    return { ...this.currentAccount };
  }

  /**
   * Resolves launcher URI for game platform using PlatformLaunchStrategy.
   * Canonical Steam App ID for Astral Party: 2622000.
   * STRICT SAFETY RULE:
   * If platform has no steamAppId (e.g. TapTap, Bilibili, WeGame), returns null (fail closed).
   * NEVER fallback to Steam URI!
   */
  public getLaunchUri(platformId: string = 'STEAM_CN'): string | null {
    const plan = resolvePlatformLaunchStrategy(platformId);
    return plan.uri;
  }

  /**
   * Triggers game launch via protocol URI.
   * In desktop Tauri environment, delegates to backend command `launch_game_uri`
   * with strict whitelist and running process duplicate prevention.
   */
  public async launchGame(
    platformId: string = 'STEAM_CN'
  ): Promise<{ uri: string | null; launched: boolean; alreadyRunning?: boolean; message?: string }> {
    const plan = resolvePlatformLaunchStrategy(platformId);
    if (!plan.canLaunch || !plan.uri) {
      return {
        uri: null,
        launched: false,
        alreadyRunning: false,
        message: plan.userMessage,
      };
    }
    const uri = plan.uri;
    let launched = false;
    let alreadyRunning = false;
    let message = '';

    // If running in Tauri desktop environment, call backend command with strict validation
    if (typeof window !== 'undefined' && (window as any).__TAURI_INTERNALS__) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        const res = await invoke<{
          success: boolean;
          uri: string;
          alreadyRunning: boolean;
          message: string;
        }>('launch_game_uri', { uri, platformId });
        return {
          uri: res.uri,
          launched: res.success,
          alreadyRunning: res.alreadyRunning,
          message: res.message,
        };
      } catch (err: any) {
        message = err?.message || String(err);
      }
    }

    // Fallback for browser / headless testing
    if (typeof window !== 'undefined' && typeof window.open === 'function') {
      window.open(uri, '_blank');
      launched = true;
    }

    return { uri, launched, alreadyRunning, message };
  }


  /**
   * Security audit validation helper.
   * Asserts that data contains NO passwords, secrets, tokens, or credential keys.
   */
  public static validateSecurityGuarantee(data: unknown): {
    isSafe: boolean;
    violations: string[];
    sanitized: UserOwnedAccountMetadata;
  } {
    const violations: string[] = [];

    if (typeof data !== 'object' || data === null) {
      return {
        isSafe: true,
        violations: [],
        sanitized: {
          platformId: 'STEAM_CN',
          lastKnownPlayerId: null,
          playerId: null,
          displayName: null,
          lastSeenAt: null,
        },
      };
    }

    const record = data as Record<string, unknown>;

    for (const key of Object.keys(record)) {
      const lowerKey = key.toLowerCase();
      for (const forbidden of FORBIDDEN_SECURITY_KEYS) {
        if (lowerKey.includes(forbidden)) {
          violations.push(key);
          break;
        }
      }
    }

    const isSafe = violations.length === 0;

    const sanitized: UserOwnedAccountMetadata = {
      platformId: typeof record.platformId === 'string' ? record.platformId : 'STEAM_CN',
      lastKnownPlayerId:
        typeof record.lastKnownPlayerId === 'number' && record.lastKnownPlayerId > 0
          ? record.lastKnownPlayerId
          : typeof record.playerId === 'number' && record.playerId > 0
          ? record.playerId
          : null,
      playerId:
        typeof record.playerId === 'number' && record.playerId > 0
          ? record.playerId
          : typeof record.lastKnownPlayerId === 'number' && record.lastKnownPlayerId > 0
          ? record.lastKnownPlayerId
          : null,
      displayName: typeof record.displayName === 'string' ? record.displayName : null,
      lastSeenAt: typeof record.lastSeenAt === 'string' ? record.lastSeenAt : null,
    };

    return { isSafe, violations, sanitized };
  }
}

/**
 * Singleton instance of UserOwnedSpectatorAccountService for Hub runtime.
 */
export const userOwnedSpectatorAccountService = new UserOwnedSpectatorAccountService();
