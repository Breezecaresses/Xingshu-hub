/**
 * Project XingShu — Game Account Identity Management Service
 * Phase: HS4-P1.4
 *
 * Implements explicit role bindings (MAIN vs USER_SPECTATOR), runtime session resolution,
 * and zero-credential persistence in LocalStorage.
 *
 * STRICT SAFETY & PRIVACY INVARIANTS:
 * 1. ZERO-CREDENTIAL: Under NO circumstances are passwords, tokens, auth keys, cookies,
 *    or session secrets accepted or stored.
 * 2. NO AUTO-INFERENCE: Player roles are NEVER automatically deduced from PlayerId.
 *    Roles MUST be explicitly bound by the user.
 * 3. RUNTIME IDENTITY TRUTH: CurrentRuntimeIdentity is strictly derived from the LIVE
 *    runtime handshake. Offline or disconnected sessions strictly resolve to null / DISCONNECTED.
 */

import {
  GameAccountRole,
  ResolvedAccountRole,
  type GameAccountBinding,
  type CurrentRuntimeIdentity,
  STORAGE_KEY_GAME_ACCOUNT_BINDINGS,
  FORBIDDEN_SECURITY_KEYS,
} from '../types/gameAccount.ts';

export type { GameAccountRole, ResolvedAccountRole, GameAccountBinding, CurrentRuntimeIdentity };

/**
 * Storage abstraction allowing isomorphic execution across Browser LocalStorage and MemoryStorage.
 */
export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

/**
 * In-memory fallback storage for headless test runners and environments without LocalStorage.
 */
export class MemoryStorage implements StorageLike {
  private store = new Map<string, string>();

  getItem(key: string): string | null {
    return this.store.has(key) ? this.store.get(key)! : null;
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  clear(): void {
    this.store.clear();
  }
}

/**
 * Game Account Identity Management Service
 */
export class GameAccountService {
  private storage: StorageLike;
  private listeners: Set<() => void> = new Set();
  private bindingsCache: GameAccountBinding[] = [];

  constructor(customStorage?: StorageLike) {
    if (customStorage) {
      this.storage = customStorage;
    } else if (typeof window !== 'undefined' && window.localStorage) {
      this.storage = window.localStorage;
    } else {
      this.storage = new MemoryStorage();
    }

    this.reloadBindings();
  }

  /**
   * Reloads bindings from storage into memory cache.
   */
  public reloadBindings(): GameAccountBinding[] {
    try {
      const raw = this.storage.getItem(STORAGE_KEY_GAME_ACCOUNT_BINDINGS);
      if (!raw) {
        this.bindingsCache = [];
        return [];
      }

      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) {
        this.bindingsCache = [];
        return [];
      }

      // Filter and sanitize each binding
      const validBindings: GameAccountBinding[] = [];
      for (const item of parsed) {
        if (
          item &&
          typeof item === 'object' &&
          typeof item.playerId === 'number' &&
          item.playerId > 0 &&
          (item.role === 'MAIN' || item.role === 'USER_SPECTATOR')
        ) {
          // Perform security validation on saved records
          GameAccountService.validateSecurity(item);

          const platformId = typeof item.platformId === 'string' ? item.platformId : 'STEAM_CN';
          const playerId = item.playerId;

          validBindings.push({
            bindingId: typeof item.bindingId === 'string' ? item.bindingId : `bind_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`,
            platformId,
            playerId,
            displayName: typeof item.displayName === 'string' ? item.displayName : null,
            role: item.role as GameAccountRole,
            boundAt: typeof item.boundAt === 'string' ? item.boundAt : new Date().toISOString(),
            lastSeenAt: typeof item.lastSeenAt === 'string' ? item.lastSeenAt : null,
          });
        }
      }

      this.bindingsCache = validBindings;
      return [...this.bindingsCache];
    } catch (err) {
      console.warn('[GameAccountService] Failed to load bindings:', err);
      this.bindingsCache = [];
      return [];
    }
  }

  /**
   * Returns a copy of all current account bindings.
   */
  public getBindings(): GameAccountBinding[] {
    return [...this.bindingsCache];
  }

  /**
   * Retrieves a binding by role ('MAIN' | 'USER_SPECTATOR').
   */
  public getBindingByRole(role: GameAccountRole): GameAccountBinding | null {
    const found = this.bindingsCache.find((b) => b.role === role);
    return found ? { ...found } : null;
  }

  /**
   * Retrieves a binding by Player ID.
   */
  public getBindingByPlayerId(playerId: number): GameAccountBinding | null {
    if (!playerId || playerId <= 0) return null;
    const found = this.bindingsCache.find((b) => b.playerId === playerId);
    return found ? { ...found } : null;
  }

  /**
   * Explicitly binds a Player ID to a specific Role.
   *
   * SAFE RE-BINDING POLICY:
   * 1. Overwrites any previous binding for the SAME role safely.
   * 2. If the same PlayerId was previously bound to another role, removes the conflicting role binding.
   * 3. Performs strict security validation ensuring ZERO secrets or tokens exist in payload.
   */
  public bindAccount(
    playerId: number,
    role: GameAccountRole,
    platformId: string = 'STEAM_CN',
    displayName?: string | null,
    extraFields?: Record<string, unknown>
  ): GameAccountBinding {
    if (!playerId || typeof playerId !== 'number' || playerId <= 0) {
      throw new Error(`[INVALID_PLAYER_ID] Player ID must be a positive integer, got: ${playerId}`);
    }

    if (role !== 'MAIN' && role !== 'USER_SPECTATOR') {
      throw new Error(`[INVALID_ROLE] Role must be 'MAIN' or 'USER_SPECTATOR', got: ${role}`);
    }

    // Security audit: reject any unauthorized credential keys
    const auditData = { playerId, role, platformId, displayName, ...(extraFields || {}) };
    const audit = GameAccountService.validateSecurity(auditData);
    if (!audit.isSafe) {
      throw new Error(
        `[SECURITY_VIOLATION] Attempted to store unauthorized credential fields: ${audit.violations.join(', ')}`
      );
    }

    const effectivePlatform = platformId.trim() || 'STEAM_CN';

    const newBinding: GameAccountBinding = {
      bindingId: `bind_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
      platformId: effectivePlatform,
      playerId,
      displayName: displayName ? displayName.trim() : null,
      role,
      boundAt: new Date().toISOString(),
      lastSeenAt: null,
    };

    // Filter out:
    // a) Any existing binding for this role (re-binding overwrites previous binding for the same role safely)
    // b) Any existing binding for this playerId under a different role (to avoid contradictory role assignments)
    const updated = this.bindingsCache.filter(
      (b) => b.role !== role && b.playerId !== playerId
    );

    updated.push(newBinding);
    this.persistBindings(updated);
    return { ...newBinding };
  }

  /**
   * Unbinds an account by its unique bindingId.
   */
  public unbindAccount(bindingId: string): void {
    const updated = this.bindingsCache.filter((b) => b.bindingId !== bindingId);
    this.persistBindings(updated);
  }

  /**
   * Unbinds any account currently assigned to the given role.
   */
  public unbindRole(role: GameAccountRole): void {
    const updated = this.bindingsCache.filter((b) => b.role !== role);
    this.persistBindings(updated);
  }

  /**
   * Resolves the active runtime session identity against bound accounts.
   *
   * RUNTIME TRUTH SPECIFICATION:
   * - If playerId is null / undefined / <= 0:
   *   -> resolvedRole is strictly 'DISCONNECTED'
   *   -> currentSessionPlayerId is strictly null
   *   -> binding is null
   * - If playerId is a valid number:
   *   -> If matching binding exists:
   *      -> resolvedRole is binding.role ('MAIN' | 'USER_SPECTATOR')
   *      -> binding is the matched binding record
   *   -> If no matching binding exists:
   *      -> resolvedRole is strictly 'UNBOUND_ACCOUNT'
   *      -> binding is null
   *
   * PROHIBITION:
   * CurrentRuntimeIdentity MUST NEVER restore currentSessionPlayerId from historical
   * storage if runtime is disconnected or hasn't provided an active playerId!
   */
  public resolveSession(
    playerId: number | null | undefined,
    platformId: string | null = null,
    runtimeConnected: boolean = false
  ): CurrentRuntimeIdentity {
    if (playerId == null || typeof playerId !== 'number' || playerId <= 0) {
      return {
        currentSessionPlayerId: null,
        platformId: platformId ?? null,
        resolvedRole: runtimeConnected
          ? ResolvedAccountRole.CONNECTED_PENDING_IDENTITY
          : ResolvedAccountRole.DISCONNECTED,
        binding: null,
      };
    }

    // STRICT IDENTITY INVARIANT:
    // Prioritize exact match on both playerId and platformId when platformId is provided
    const matchedBinding =
      (platformId ? this.bindingsCache.find((b) => b.playerId === playerId && b.platformId === platformId) : null) ||
      this.bindingsCache.find((b) => b.playerId === playerId);

    if (matchedBinding) {
      return {
        currentSessionPlayerId: playerId,
        platformId: platformId ?? matchedBinding.platformId,
        resolvedRole: matchedBinding.role,
        binding: { ...matchedBinding },
      };
    }

    return {
      currentSessionPlayerId: playerId,
      platformId: platformId ?? null,
      resolvedRole: ResolvedAccountRole.UNBOUND_ACCOUNT,
      binding: null,
    };
  }

  /**
   * Updates lastSeenAt timestamp for a player ID when observed active in runtime.
   */
  public touchLastSeen(playerId: number): void {
    if (!playerId || playerId <= 0) return;
    let modified = false;
    const now = new Date().toISOString();

    const updated = this.bindingsCache.map((b) => {
      if (b.playerId === playerId) {
        modified = true;
        return { ...b, lastSeenAt: now };
      }
      return b;
    });

    if (modified) {
      this.persistBindings(updated);
    }
  }

  /**
   * Clears all bindings and empties storage.
   */
  public clearAllBindings(): void {
    this.persistBindings([]);
  }

  /**
   * Subscribes to changes in bindings (e.g., for React UI hooks).
   */
  public subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  /**
   * Internal persistence helper with whitelist sanitization and credential audit.
   */
  private persistBindings(bindings: GameAccountBinding[]): void {
    // Whitelist sanitization: ONLY safe properties are preserved
    const sanitizedList: GameAccountBinding[] = bindings.map((b) => ({
      bindingId: b.bindingId,
      platformId: b.platformId,
      playerId: b.playerId,
      displayName: b.displayName ?? null,
      role: b.role,
      boundAt: b.boundAt,
      lastSeenAt: b.lastSeenAt ?? null,
    }));

    const json = JSON.stringify(sanitizedList);

    // Double check serialized output for any forbidden security key tokens
    for (const forbidden of FORBIDDEN_SECURITY_KEYS) {
      const regex = new RegExp(`"${forbidden}"`, 'i');
      if (regex.test(json)) {
        throw new Error(
          `[SECURITY_VIOLATION] Serialized bindings JSON contains forbidden credential key: "${forbidden}"`
        );
      }
    }

    this.storage.setItem(STORAGE_KEY_GAME_ACCOUNT_BINDINGS, json);
    this.bindingsCache = sanitizedList;
    this.notifyListeners();
  }

  private notifyListeners(): void {
    for (const listener of this.listeners) {
      try {
        listener();
      } catch (e) {
        console.error('[GameAccountService] Listener error:', e);
      }
    }
  }

  /**
   * Validates that an object contains NO passwords, secrets, tokens, or credential keys.
   */
  public static validateSecurity(data: unknown): {
    isSafe: boolean;
    violations: string[];
  } {
    const violations: string[] = [];

    if (typeof data !== 'object' || data === null) {
      return { isSafe: true, violations: [] };
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

    return {
      isSafe: violations.length === 0,
      violations,
    };
  }
}

/**
 * Singleton instance of GameAccountService for application-wide use.
 */
export const gameAccountService = new GameAccountService();
