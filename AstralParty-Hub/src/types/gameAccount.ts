/**
 * Project XingShu — Game Account Identity Model Definitions
 * Phase: HS4-P1.4
 *
 * Defines explicit role bindings, runtime session identities, and zero-credential
 * boundaries between the user's primary game account ('MAIN') and dedicated spectator
 * account ('USER_SPECTATOR').
 *
 * STRICT SAFETY & PRIVACY GUARANTEES:
 * 1. Zero credential storage: Prohibits storing passwords, Steam tokens, auth cookies, or session secrets.
 * 2. Explicit role binding: Account roles MUST NEVER be automatically inferred or guessed.
 *    Roles must be explicitly bound and configured by the user.
 * 3. Runtime identity truth: The active session identity is strictly derived from the LIVE
 *    runtime handshake, NEVER restored from historical storage if runtime is disconnected.
 */

/**
 * Functional roles for bound game accounts.
 * - MAIN: Primary account used by player for ladder/casual matches, progression, and daily play.
 * - USER_SPECTATOR: Dedicated second account owned by the user for spectator/broadcast purposes.
 */
export type GameAccountRole = 'MAIN' | 'USER_SPECTATOR';

export const GameAccountRole = {
  MAIN: 'MAIN' as GameAccountRole,
  USER_SPECTATOR: 'USER_SPECTATOR' as GameAccountRole,
} as const;

/**
 * Resolved role classification for the active runtime session.
 * - MAIN: Runtime player ID matches user's bound primary account.
 * - USER_SPECTATOR: Runtime player ID matches user's bound spectator account.
 * - UNBOUND_ACCOUNT: Game is running and player ID is recognized, but not bound to any role.
 * - DISCONNECTED: Runtime is not running or hasn't provided an active session player ID.
 */
export type ResolvedAccountRole =
  | GameAccountRole
  | 'UNBOUND_ACCOUNT'
  | 'DISCONNECTED'
  | 'CONNECTED_PENDING_IDENTITY';

export const ResolvedAccountRole = {
  MAIN: 'MAIN' as ResolvedAccountRole,
  USER_SPECTATOR: 'USER_SPECTATOR' as ResolvedAccountRole,
  UNBOUND_ACCOUNT: 'UNBOUND_ACCOUNT' as ResolvedAccountRole,
  DISCONNECTED: 'DISCONNECTED' as ResolvedAccountRole,
  CONNECTED_PENDING_IDENTITY: 'CONNECTED_PENDING_IDENTITY' as ResolvedAccountRole,
} as const;

/**
 * Persistent explicit binding between a game account (PlayerId + Platform) and a system role.
 * Contains ZERO secrets, tokens, or credentials.
 */
export interface GameAccountBinding {
  /** Unique identifier for this binding record (uuid or timestamp-based) */
  bindingId: string;
  /** Game platform identifier, e.g. 'STEAM_CN', 'STEAM_GLOBAL' */
  platformId: string;
  /** Game authorative 32-bit integer Player ID (public in-game identifier) */
  playerId: number;
  /** In-game display name or user-assigned nickname */
  displayName: string | null;
  /** Explicitly assigned functional role */
  role: GameAccountRole;
  /** ISO-8601 timestamp when this binding was created or updated */
  boundAt: string;
  /** ISO-8601 timestamp when this account was last observed active in runtime */
  lastSeenAt: string | null;
}

/**
 * Live runtime session identity state.
 * Strictly derived from live runtime signals; NEVER restored from offline persistence.
 */
export interface CurrentRuntimeIdentity {
  /**
   * Live authorative player ID received in the current active runtime session.
   * Strictly null if runtime is disconnected or packet not yet received.
   */
  currentSessionPlayerId: number | null;
  /**
   * Target platform identifier of the current runtime session.
   */
  platformId: string | null;
  /**
   * Resolved identity role based on currentSessionPlayerId vs user's explicit bindings.
   */
  resolvedRole: ResolvedAccountRole;
  /**
   * The matched GameAccountBinding record if resolvedRole is MAIN or USER_SPECTATOR, else null.
   */
  binding: GameAccountBinding | null;
}

/**
 * Standard storage key for game account bindings in LocalStorage.
 */
export const STORAGE_KEY_GAME_ACCOUNT_BINDINGS = 'xingshu_game_account_bindings';

/**
 * Forbidden security keys used in security audits to prevent credential leaks.
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
  'key',
] as const;

/**
 * Localized role display labels.
 */
export const ROLE_LABELS_ZH: Record<ResolvedAccountRole, string> = {
  MAIN: '主账号',
  USER_SPECTATOR: '我的观战账号',
  UNBOUND_ACCOUNT: '未绑定账号',
  DISCONNECTED: '未连接 Runtime',
  CONNECTED_PENDING_IDENTITY: 'Runtime 已连接（等待账号识别）',
};

export const ROLE_LABELS_EN: Record<ResolvedAccountRole, string> = {
  MAIN: 'Main Account',
  USER_SPECTATOR: 'Dedicated Spectator Account',
  UNBOUND_ACCOUNT: 'Unbound Account',
  DISCONNECTED: 'Disconnected',
  CONNECTED_PENDING_IDENTITY: 'Connected (Pending Identity)',
};
