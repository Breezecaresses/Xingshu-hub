/**
 * Project XingShu — Runtime Session & Routing Type Definitions
 * Phase: HS4-P1.5
 * Lane 4: Account-to-Runtime Routing Specialist
 *
 * Defines models and contracts for multi-client runtime sessions, account-to-runtime
 * routing resolution, and explicit spectator execution runtime selection.
 *
 * STRICT INVARIANTS:
 * 1. Zero credential storage: Passwords, tokens, cookies, and secrets are forbidden.
 * 2. Explicit role binding: Account roles are NEVER guessed. Routing respects explicit user bindings:
 *    - MAIN: e.g. STEAM_CN
 *    - USER_SPECTATOR: e.g. TAPTAP
 * 3. Never resolve routing solely on "last connected Runtime":
 *    Spectator execution MUST strictly target the dedicated TapTap spectator runtime.
 * 4. Dual-client coexistence: If only Steam MAIN is connected, prompt user to launch TapTap
 *    with dedicated spectator account. Strictly prohibit prompting the user to log out of Steam.
 */

import type { GameAccountBinding } from './gameAccount';

/**
 * Authoritative Runtime Match Context representing current match state for a connected runtime.
 */
export interface RuntimeMatchContext {
  /** Connection ID of the runtime */
  connectionId: string;
  /** Platform identifier, e.g. 'STEAM_CN', 'TAPTAP' */
  platformId: string;
  /** Operating system process ID */
  processId: number;
  /** Player ID observed in this runtime */
  localPlayerId?: number | null;
  /** High-level match state: MAIN_MENU, LOBBY, MATCH_LOADING, MAIN_IN_MATCH, MATCH_ENDED */
  matchState: string;
  /** Match kind: 'PVE', 'PVP', or null */
  matchKind?: 'PVE' | 'PVP' | string | null;
  /** Internal Room ID: number | null */
  internalRoomId?: number | null;
  /** Public Spectator Code: STRING | null. Never an integer. */
  spectatorCode?: string | null;
  /** Player count in room */
  playerCount: number;
  /** Array of player IDs in room roster */
  rosterPlayerIds: number[];
  /** Current scene name if available */
  scene?: string | null;
  /** Epoch timestamp in ms */
  timestamp: number;
  /** Sequence number */
  sequence: number;

  // Compatibility fields
  roomId?: number;
  inMatch?: boolean;
  isPve?: boolean;
}

/**
 * Raw runtime session connected via TCP bridge or IPC.
 */
export interface RuntimeSession {
  /** Unique connection identifier (socket id or connection token) */
  connectionId: string;
  /** Platform identifier, e.g. 'STEAM_CN', 'TAPTAP', 'STEAM_GLOBAL' */
  platformId: string;
  /** Game client operating system process ID */
  processId: number;
  /** Alternative alias for processId */
  gamePid?: number;
  /** Process ID alias */
  pid?: number;
  /** Executable path of the game process */
  executablePath?: string;
  /** Generation counter of this runtime connection */
  generation?: number;
  /** Timestamp when runtime session was last observed */
  lastSeenAt?: string | number;
  /** Assigned role */
  role?: string;
  /** Bound role: 'MAIN', 'USER_SPECTATOR', 'UNBOUND' */
  boundRole?: string;
  /** Expected bound player ID for this platform */
  expectedBoundPlayerId?: number | null;
  /**
   * Authoritative player ID received from live handshake/packet.
   * Null or undefined if the identity packet is still pending.
   */
  playerId: number | null;
  /** Live Player ID alias */
  livePlayerId?: number | null;
  /** Player in-game nickname or display name if recognized */
  displayName?: string | null;
  /** Timestamp when runtime connected */
  connectedAt?: string | number;
  /** Timestamp of most recent heartbeat */
  lastHeartbeatAt?: string | number;
  /** Connection state */
  status?: 'CONNECTED' | 'DISCONNECTED' | 'CONNECTING';
  /** Connection state alias */
  connectionState?: string;
  /** Local port of the runtime TCP link */
  port?: number;
  /** Version of injected XingShu runtime */
  runtimeVersion?: string;

  /** Active match context scoped strictly to this connection */
  matchContext?: RuntimeMatchContext | null;
  /** Internal Room ID (number | null) */
  internalRoomId?: number | null;
  /** Public Spectator Code (string | null) */
  spectatorCode?: string | null;
  /** Legacy room ID */
  roomId?: number;
  /** Whether runtime is actively in match */
  inMatch?: boolean;
  /** Whether match is PVE */
  isPve?: boolean;
  /** Timestamp of last match context update */
  matchContextUpdatedAt?: number;
}

/**
 * Resolved role classification for an active runtime session.
 * - MAIN: Matches explicit binding for primary game account.
 * - USER_SPECTATOR: Matches explicit binding for dedicated spectator account.
 * - PENDING_SPECTATOR: Tentative match on spectator platform (TapTap) while playerId is pending.
 * - PENDING_MAIN: Tentative match on main platform (Steam CN) while playerId is pending.
 * - PENDING_ROLE: Generic tentative match on platform before identity packet arrives.
 * - UNBOUND: Live session is recognized, but does not match any role binding.
 * - UNBOUND_ACCOUNT: Alias for UNBOUND for compatibility with HS4-P1.4 naming.
 * - DISCONNECTED: Session is offline or closed.
 */
export type ResolvedRuntimeRole =
  | 'MAIN'
  | 'USER_SPECTATOR'
  | 'PENDING_SPECTATOR'
  | 'PENDING_MAIN'
  | 'PENDING_ROLE'
  | 'UNBOUND'
  | 'UNBOUND_ACCOUNT'
  | 'DISCONNECTED';

export const ResolvedRuntimeRole = {
  MAIN: 'MAIN' as ResolvedRuntimeRole,
  USER_SPECTATOR: 'USER_SPECTATOR' as ResolvedRuntimeRole,
  PENDING_SPECTATOR: 'PENDING_SPECTATOR' as ResolvedRuntimeRole,
  PENDING_MAIN: 'PENDING_MAIN' as ResolvedRuntimeRole,
  PENDING_ROLE: 'PENDING_ROLE' as ResolvedRuntimeRole,
  UNBOUND: 'UNBOUND' as ResolvedRuntimeRole,
  UNBOUND_ACCOUNT: 'UNBOUND_ACCOUNT' as ResolvedRuntimeRole,
  DISCONNECTED: 'DISCONNECTED' as ResolvedRuntimeRole,
} as const;

/**
 * Fully resolved runtime session decorated with matched binding and role metadata.
 */
export interface ResolvedRuntimeSession {
  /** Underlying connection ID */
  connectionId: string;
  /** Platform identifier, e.g. 'STEAM_CN', 'TAPTAP' */
  platformId: string;
  /** Process ID */
  processId: number;
  /** Authoritative player ID or null if pending */
  playerId: number | null;
  /** Display name if available */
  displayName: string | null;
  /** Assigned functional role */
  role: ResolvedRuntimeRole;
  /** Assigned functional role (alias for role) */
  resolvedRole: ResolvedRuntimeRole;
  /** Matched GameAccountBinding, or null if unbound */
  matchedBinding: GameAccountBinding | null;
  /** Flag indicating whether this session is the active spectator execution runtime */
  isSpectatorExecutionRuntime: boolean;
  /** Connection status */
  status: 'CONNECTED' | 'DISCONNECTED';
  /** Chinese role label */
  roleLabelZh: string;
  /** English role label */
  roleLabelEn: string;
  /** Original raw session */
  session: RuntimeSession;
}

/**
 * Result of resolving multiple active runtime sessions against user bindings.
 */
export interface RuntimeResolutionResult {
  /** All resolved active runtime sessions */
  allSessions: ResolvedRuntimeSession[];
  /** Session resolved as MAIN account, if connected */
  mainSession: ResolvedRuntimeSession | null;
  /** Session resolved as USER_SPECTATOR account, if connected */
  spectatorSession: ResolvedRuntimeSession | null;
  /** Selected runtime session for spectator execution */
  selectedSpectatorSession: ResolvedRuntimeSession | null;
  /** Connection ID of selected spectator execution runtime */
  selectedSpectatorConnectionId: string | null;
  /**
   * Prompts user when dedicated spectator runtime is missing or pending.
   * e.g., "专用观战账号尚未启动。请启动 TapTap 客户端并登录观战账号"
   */
  spectatorPrompt: string | null;
  /** Whether a role conflict or configuration error exists */
  hasRoleMismatch: boolean;
  /** Whether the spectator execution pipeline is ready for hand streaming */
  isSpectatorReady: boolean;
}

/**
 * Default explicit canonical bindings.
 * Production defaults must be empty; bindings are configured exclusively via GameAccountService.
 */
export const DEFAULT_CANONICAL_BINDINGS: readonly GameAccountBinding[] = [];

/**
 * Standard prompt message when dedicated TapTap spectator runtime is offline.
 */
export const TAPTAP_SPECTATOR_LAUNCH_PROMPT =
  '专用观战账号尚未启动。请启动 TapTap 客户端并登录观战账号';

/**
 * Localized role labels for UI display.
 */
export const RUNTIME_ROLE_LABELS_ZH: Record<ResolvedRuntimeRole, string> = {
  MAIN: '主账号',
  USER_SPECTATOR: '我的观战账号',
  PENDING_SPECTATOR: '观战账号 (等待识别)',
  PENDING_MAIN: '主账号 (等待识别)',
  PENDING_ROLE: '角色待定',
  UNBOUND: '未绑定账号',
  UNBOUND_ACCOUNT: '未绑定账号',
  DISCONNECTED: '未连接 Runtime',
};

export const RUNTIME_ROLE_LABELS_EN: Record<ResolvedRuntimeRole, string> = {
  MAIN: 'Main Account',
  USER_SPECTATOR: 'Dedicated Spectator Account',
  PENDING_SPECTATOR: 'Spectator Account (Pending Identity)',
  PENDING_MAIN: 'Main Account (Pending Identity)',
  PENDING_ROLE: 'Pending Role',
  UNBOUND: 'Unbound Account',
  UNBOUND_ACCOUNT: 'Unbound Account',
  DISCONNECTED: 'Disconnected',
};
