/**
 * Project XingShu — Auto-Follow Spectator State Machine
 * Phase: HS4-P1.6
 *
 * Implements the product-complete Auto-Follow Spectator State Machine
 * and Local Control Protocol (Hub -> Runtime) with strict security boundaries:
 * - Hub may send WATCH_TARGET ONLY to verified TAPTAP RuntimeSession matching
 *   bound USER_SPECTATOR (3045259).
 * - NEVER route WATCH_TARGET to Steam MAIN (1556514).
 *
 * All 11 States:
 * - IDLE
 * - MAIN_IN_MATCH
 * - SPECTATOR_CLIENT_OFFLINE
 * - LAUNCHING_SPECTATOR_CLIENT
 * - WAITING_SPECTATOR_RUNTIME
 * - WAITING_SPECTATOR_LOGIN
 * - SPECTATOR_READY
 * - AUTO_JOINING
 * - SPECTATING
 * - AUTO_JOIN_FAILED
 * - MAIN_MATCH_ENDED
 */

export type AutoFollowState =
  | 'IDLE'
  | 'MAIN_IN_MATCH'
  | 'SPECTATOR_CLIENT_OFFLINE'
  | 'LAUNCHING_SPECTATOR_CLIENT'
  | 'WAITING_SPECTATOR_RUNTIME'
  | 'WAITING_SPECTATOR_LOGIN'
  | 'SPECTATOR_READY'
  | 'AUTO_JOINING'
  | 'SPECTATING'
  | 'AUTO_JOIN_FAILED'
  | 'MAIN_MATCH_ENDED';

import { CANONICAL_BOUND_MAIN_PLAYER_ID, CANONICAL_BOUND_SPECTATOR_PLAYER_ID } from './expectedAccountBindings.ts';
export { CANONICAL_BOUND_MAIN_PLAYER_ID, CANONICAL_BOUND_SPECTATOR_PLAYER_ID } from './expectedAccountBindings.ts';
export const CANONICAL_SPECTATOR_PLATFORM = 'TAPTAP';
export const CANONICAL_MAIN_PLATFORM = 'STEAM_CN';

export type WatchTargetResponseType =
  | 'WATCH_JOIN_ACCEPTED'
  | 'WATCH_JOIN_REJECTED'
  | 'WATCH_JOIN_TIMEOUT'
  | 'WATCH_ALREADY_JOINED'
  | 'WATCH_TARGET_INVALID';

export interface WatchTargetPrimitive {
  roomId: number;
  internalRoomId?: number | null;
  spectatorCode?: string | null;
  watchCode?: string | null;
  matchKind?: string | null;
  playerCount?: number;
  rosterPlayerIds?: number[];
  roomServerId?: number;
  mapId?: number;
}

export interface WatchTargetPayload {
  target: WatchTargetPrimitive | string | number;
  requestId: string;
  timestamp: number;
  targetPlatform: string;
  expectedSpectatorPlayerId: number;
}

export interface WatchTargetResponse {
  status: WatchTargetResponseType;
  requestId: string;
  target?: any;
  reason?: string;
  timestamp?: number;
}

export type AutoFollowFailureReason =
  | 'CLIENT_LAUNCH_FAILED'
  | 'RUNTIME_CONNECT_TIMEOUT'
  | 'LOGIN_TIMEOUT'
  | 'WRONG_ACCOUNT_LOGGED_IN'
  | 'WATCH_JOIN_REJECTED'
  | 'WATCH_JOIN_TIMEOUT'
  | 'WATCH_TARGET_INVALID'
  | 'DISCONNECTED_DURING_JOIN'
  | 'SPECTATOR_DISCONNECTED'
  | 'SECURITY_VIOLATION'
  | 'UNKNOWN_ERROR';

export interface StateHistoryEntry {
  from: AutoFollowState;
  to: AutoFollowState;
  trigger: string;
  timestamp: number;
}

export interface AutoFollowContext {
  currentState: AutoFollowState;
  previousState: AutoFollowState | null;
  mainPlayerId: number;
  expectedSpectatorPlayerId: number;
  targetPlatform: string;
  activeTarget: WatchTargetPrimitive | null;
  spectatorPid: number | null;
  spectatorConnectionId: string | null;
  spectatorPlayerId: number | null;
  lastRequestId: string | null;
  failureReason: AutoFollowFailureReason | null;
  failureMessage: string | null;
  retryCount: number;
  manualFallbackCode: string | null;
  stateTimestamp: number;
  history: StateHistoryEntry[];
}

export type AutoFollowEvent =
  | { type: 'MAIN_MATCH_DETECTED'; target: WatchTargetPrimitive; mainPlayerId?: number }
  | { type: 'PROCESS_STATUS'; isRunning: boolean; pid?: number }
  | { type: 'CLIENT_LAUNCH_INITIATED' }
  | { type: 'CLIENT_LAUNCH_TIMEOUT' }
  | { type: 'RUNTIME_CONNECTED'; connectionId: string; platformId: string; playerId?: number | null }
  | { type: 'RUNTIME_TIMEOUT' }
  | { type: 'IDENTITY_RESOLVED'; playerId: number }
  | { type: 'LOGIN_TIMEOUT' }
  | { type: 'WATCH_TARGET_DISPATCHED'; requestId: string; connectionId: string }
  | { type: 'WATCH_RESPONSE_RECEIVED'; response: WatchTargetResponse }
  | { type: 'HAND_SNAPSHOT_RECEIVED'; roomId: number; epoch?: number; sequence?: number }
  | { type: 'SPECTATOR_DISCONNECTED'; connectionId?: string }
  | { type: 'MAIN_MATCH_ENDED'; reason?: string }
  | { type: 'RETRY_AUTO_JOIN' }
  | { type: 'MANUAL_FALLBACK_CODE_ENTERED'; watchCode: string; roomId?: number }
  | { type: 'RESET_TO_IDLE' };

export interface AutoFollowTransitionResult {
  previousState: AutoFollowState;
  nextState: AutoFollowState;
  changed: boolean;
  context: AutoFollowContext;
  actionRequired?: 'LAUNCH_CLIENT' | 'SEND_WATCH_TARGET' | 'SHOW_ERROR' | 'NONE';
  errorMessage?: string;
}

/**
 * Strict Security Guard enforcing local control boundaries:
 * Hub may send WATCH_TARGET ONLY to verified TAPTAP RuntimeSession matching
 * bound USER_SPECTATOR 3045259. Never route to Steam MAIN.
 */
export function verifyWatchTargetSecurity(
  session: {
    connectionId?: string | null;
    platformId?: string | null;
    playerId?: number | null;
  },
  expectedSpectatorPlayerId: number = CANONICAL_BOUND_SPECTATOR_PLAYER_ID,
  expectedPlatform: string = CANONICAL_SPECTATOR_PLATFORM
): { allowed: boolean; violationReason?: string } {
  if (!session || !session.connectionId) {
    return {
      allowed: false,
      violationReason: 'SECURITY_VIOLATION: No active runtime connection specified.',
    };
  }

  // 1. Platform Invariant: Must be TAPTAP
  if (session.platformId !== expectedPlatform) {
    return {
      allowed: false,
      violationReason: `SECURITY_VIOLATION: Destination platform is '${session.platformId}'. Destination must match the bound spectator platform '${expectedPlatform}' (player ${session.playerId ?? 'Unknown'}).`,
    };
  }

  // Pending identity may be shown as connected, but must never receive a watch command.
  if (!session.playerId || session.playerId <= 0 || session.playerId !== expectedSpectatorPlayerId) {
      return {
        allowed: false,
        violationReason: `SECURITY_VIOLATION: Runtime player ID ${session.playerId} does not match bound USER_SPECTATOR (${expectedSpectatorPlayerId})!`,
      };
  }

  return { allowed: true };
}

/**
 * Creates the initial context for the AutoFollow state machine.
 */
export function createInitialAutoFollowContext(
  overrides?: Partial<AutoFollowContext>
): AutoFollowContext {
  return {
    currentState: 'IDLE',
    previousState: null,
    mainPlayerId: CANONICAL_BOUND_MAIN_PLAYER_ID,
    expectedSpectatorPlayerId: CANONICAL_BOUND_SPECTATOR_PLAYER_ID,
    targetPlatform: CANONICAL_SPECTATOR_PLATFORM,
    activeTarget: null,
    spectatorPid: null,
    spectatorConnectionId: null,
    spectatorPlayerId: null,
    lastRequestId: null,
    failureReason: null,
    failureMessage: null,
    retryCount: 0,
    manualFallbackCode: null,
    stateTimestamp: Date.now(),
    history: [],
    ...overrides,
  };
}

/**
 * Pure state machine transition function.
 * Deterministic and free of side effects; easily unit-tested in isolation.
 */
export function processAutoFollowTransition(
  context: AutoFollowContext,
  event: AutoFollowEvent,
  nowMs: number = Date.now()
): AutoFollowTransitionResult {
  const current = context.currentState;
  let next = current;
  let action: 'LAUNCH_CLIENT' | 'SEND_WATCH_TARGET' | 'SHOW_ERROR' | 'NONE' = 'NONE';
  let nextCtx: AutoFollowContext = { ...context };

  if ((event.type === 'SPECTATOR_DISCONNECTED' && event.connectionId && event.connectionId !== context.spectatorConnectionId) ||
      (event.type === 'WATCH_RESPONSE_RECEIVED' && context.lastRequestId && event.response.requestId !== context.lastRequestId)) {
    return { previousState: current, nextState: current, changed: false, context, actionRequired: 'NONE' };
  }
  if (event.type === 'PROCESS_STATUS') nextCtx.spectatorPid = event.isRunning ? (event.pid ?? context.spectatorPid) : null;
  if (event.type === 'RUNTIME_CONNECTED' && event.platformId === nextCtx.targetPlatform) {
    nextCtx.spectatorConnectionId = event.connectionId;
    nextCtx.spectatorPlayerId = event.playerId ?? null;
  }

  if (event.type === 'MAIN_MATCH_DETECTED') {
    nextCtx.activeTarget = { ...event.target };
    if (event.mainPlayerId) nextCtx.mainPlayerId = event.mainPlayerId;
  }

  switch (current) {
    // -------------------------------------------------------------------------
    // 1. IDLE
    // -------------------------------------------------------------------------
    case 'IDLE': {
      if (event.type === 'MAIN_MATCH_DETECTED') {
        // Evaluate spectator readiness immediately
        const hasPid = typeof nextCtx.spectatorPid === 'number' && nextCtx.spectatorPid > 0;
        const hasConn = !!nextCtx.spectatorConnectionId;
        const hasExpectedId = nextCtx.spectatorPlayerId === nextCtx.expectedSpectatorPlayerId;

        if (hasConn && hasExpectedId) {
          next = 'SPECTATOR_READY';
          action = 'SEND_WATCH_TARGET';
        } else if (hasConn && nextCtx.spectatorPlayerId === null) {
          next = 'WAITING_SPECTATOR_LOGIN';
        } else if (hasPid && !hasConn) {
          next = 'WAITING_SPECTATOR_RUNTIME';
        } else {
          // Client is offline or unverified
          next = 'SPECTATOR_CLIENT_OFFLINE';
          action = 'LAUNCH_CLIENT';
        }
      } else if (event.type === 'PROCESS_STATUS') {
        nextCtx.spectatorPid = event.isRunning ? (event.pid ?? nextCtx.spectatorPid ?? 1) : null;
      } else if (event.type === 'RUNTIME_CONNECTED') {
        if (event.platformId === nextCtx.targetPlatform) {
          nextCtx.spectatorConnectionId = event.connectionId;
          if (typeof event.playerId === 'number' && event.playerId > 0) {
            nextCtx.spectatorPlayerId = event.playerId;
          }
        }
      } else if (event.type === 'SPECTATOR_DISCONNECTED') {
        if (event.connectionId === nextCtx.spectatorConnectionId || !event.connectionId) {
          nextCtx.spectatorConnectionId = null;
        }
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 2. MAIN_IN_MATCH
    // -------------------------------------------------------------------------
    case 'MAIN_IN_MATCH': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'PROCESS_STATUS') {
        nextCtx.spectatorPid = event.isRunning ? (event.pid ?? nextCtx.spectatorPid) : null;
        if (!event.isRunning) {
          next = 'SPECTATOR_CLIENT_OFFLINE';
          action = 'LAUNCH_CLIENT';
        }
      } else if (event.type === 'CLIENT_LAUNCH_INITIATED') {
        next = 'LAUNCHING_SPECTATOR_CLIENT';
      } else if (event.type === 'RUNTIME_CONNECTED') {
        if (event.platformId === nextCtx.targetPlatform) {
          nextCtx.spectatorConnectionId = event.connectionId;
          if (event.playerId === nextCtx.expectedSpectatorPlayerId) {
            nextCtx.spectatorPlayerId = event.playerId;
            next = 'SPECTATOR_READY';
            action = 'SEND_WATCH_TARGET';
          } else if (event.playerId && event.playerId !== nextCtx.expectedSpectatorPlayerId) {
            nextCtx.spectatorPlayerId = event.playerId;
            next = 'AUTO_JOIN_FAILED';
            nextCtx.failureReason = 'WRONG_ACCOUNT_LOGGED_IN';
            nextCtx.failureMessage = `已登录账号 (${event.playerId}) 与绑定的观战账号 (${nextCtx.expectedSpectatorPlayerId}) 不符`;
          } else {
            next = 'WAITING_SPECTATOR_LOGIN';
          }
        }
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 3. SPECTATOR_CLIENT_OFFLINE
    // -------------------------------------------------------------------------
    case 'SPECTATOR_CLIENT_OFFLINE': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'CLIENT_LAUNCH_INITIATED') {
        next = 'LAUNCHING_SPECTATOR_CLIENT';
      } else if (event.type === 'PROCESS_STATUS' && event.isRunning) {
        nextCtx.spectatorPid = event.pid ?? 1;
        next = 'WAITING_SPECTATOR_RUNTIME';
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 4. LAUNCHING_SPECTATOR_CLIENT
    // -------------------------------------------------------------------------
    case 'LAUNCHING_SPECTATOR_CLIENT': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'PROCESS_STATUS' && event.isRunning) {
        nextCtx.spectatorPid = event.pid ?? nextCtx.spectatorPid ?? 1;
        next = 'WAITING_SPECTATOR_RUNTIME';
      } else if (event.type === 'RUNTIME_CONNECTED' && event.platformId === nextCtx.targetPlatform) {
        nextCtx.spectatorConnectionId = event.connectionId;
        if (event.playerId === nextCtx.expectedSpectatorPlayerId) {
          nextCtx.spectatorPlayerId = event.playerId;
          next = 'SPECTATOR_READY';
          action = 'SEND_WATCH_TARGET';
        } else if (event.playerId && event.playerId !== nextCtx.expectedSpectatorPlayerId) {
          nextCtx.spectatorPlayerId = event.playerId;
          next = 'AUTO_JOIN_FAILED';
          nextCtx.failureReason = 'WRONG_ACCOUNT_LOGGED_IN';
          nextCtx.failureMessage = `已登录账号 (${event.playerId}) 与绑定的观战账号 (${nextCtx.expectedSpectatorPlayerId}) 不符`;
        } else {
          next = 'WAITING_SPECTATOR_LOGIN';
        }
      } else if (event.type === 'CLIENT_LAUNCH_TIMEOUT') {
        next = 'AUTO_JOIN_FAILED';
        nextCtx.failureReason = 'CLIENT_LAUNCH_FAILED';
        nextCtx.failureMessage = '已绑定观战客户端启动超时，未检测到进程运行';
        action = 'SHOW_ERROR';
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 5. WAITING_SPECTATOR_RUNTIME
    // -------------------------------------------------------------------------
    case 'WAITING_SPECTATOR_RUNTIME': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'RUNTIME_CONNECTED' && event.platformId === nextCtx.targetPlatform) {
        nextCtx.spectatorConnectionId = event.connectionId;
        if (event.playerId === nextCtx.expectedSpectatorPlayerId) {
          nextCtx.spectatorPlayerId = event.playerId;
          next = 'SPECTATOR_READY';
          action = 'SEND_WATCH_TARGET';
        } else if (event.playerId && event.playerId !== nextCtx.expectedSpectatorPlayerId) {
          nextCtx.spectatorPlayerId = event.playerId;
          next = 'AUTO_JOIN_FAILED';
          nextCtx.failureReason = 'WRONG_ACCOUNT_LOGGED_IN';
          nextCtx.failureMessage = `已登录账号 (${event.playerId}) 与绑定的观战账号 (${nextCtx.expectedSpectatorPlayerId}) 不符`;
          action = 'SHOW_ERROR';
        } else {
          next = 'WAITING_SPECTATOR_LOGIN';
        }
      } else if (event.type === 'RUNTIME_TIMEOUT') {
        next = 'AUTO_JOIN_FAILED';
        nextCtx.failureReason = 'RUNTIME_CONNECT_TIMEOUT';
        nextCtx.failureMessage = '观战客户端已启动，但 APX 运行时握手通讯超时';
        action = 'SHOW_ERROR';
      } else if (event.type === 'PROCESS_STATUS' && !event.isRunning) {
        nextCtx.spectatorPid = null;
        next = 'SPECTATOR_CLIENT_OFFLINE';
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 6. WAITING_SPECTATOR_LOGIN
    // -------------------------------------------------------------------------
    case 'WAITING_SPECTATOR_LOGIN': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'IDENTITY_RESOLVED') {
        if (event.playerId === nextCtx.expectedSpectatorPlayerId) {
          nextCtx.spectatorPlayerId = event.playerId;
          next = 'SPECTATOR_READY';
          action = 'SEND_WATCH_TARGET';
        } else {
          nextCtx.spectatorPlayerId = event.playerId;
          next = 'AUTO_JOIN_FAILED';
          nextCtx.failureReason = 'WRONG_ACCOUNT_LOGGED_IN';
          nextCtx.failureMessage = `已登录账号 (${event.playerId}) 与绑定的观战账号 (${nextCtx.expectedSpectatorPlayerId}) 不符`;
          action = 'SHOW_ERROR';
        }
      } else if (event.type === 'LOGIN_TIMEOUT') {
        next = 'AUTO_JOIN_FAILED';
        nextCtx.failureReason = 'LOGIN_TIMEOUT';
        nextCtx.failureMessage = '等待观战账号登录超时，请在 观战客户端中完成登录';
        action = 'SHOW_ERROR';
      } else if (event.type === 'SPECTATOR_DISCONNECTED') {
        nextCtx.spectatorConnectionId = null;
        next = 'WAITING_SPECTATOR_RUNTIME';
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 7. SPECTATOR_READY
    // -------------------------------------------------------------------------
    case 'SPECTATOR_READY': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'WATCH_TARGET_DISPATCHED') {
        nextCtx.lastRequestId = event.requestId;
        next = 'AUTO_JOINING';
      } else if (event.type === 'SPECTATOR_DISCONNECTED') {
        nextCtx.spectatorConnectionId = null;
        next = 'WAITING_SPECTATOR_RUNTIME';
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 8. AUTO_JOINING
    // -------------------------------------------------------------------------
    case 'AUTO_JOINING': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        // Match ended during join
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'WATCH_RESPONSE_RECEIVED') {
        const resp = event.response;
        if (resp.status === 'WATCH_JOIN_ACCEPTED') {
          // Accepted by runtime, waiting for first hand snapshot to confirm spectating
        } else if (resp.status === 'WATCH_ALREADY_JOINED') {
          next = 'SPECTATING';
        } else if (resp.status === 'WATCH_JOIN_REJECTED') {
          next = 'AUTO_JOIN_FAILED';
          nextCtx.failureReason = 'WATCH_JOIN_REJECTED';
          nextCtx.failureMessage = resp.reason || '观战加入请求被服务端拒绝 (房间观战人数可能已满或无权限)';
          action = 'SHOW_ERROR';
        } else if (resp.status === 'WATCH_JOIN_TIMEOUT') {
          next = 'AUTO_JOIN_FAILED';
          nextCtx.failureReason = 'WATCH_JOIN_TIMEOUT';
          nextCtx.failureMessage = '游戏服务器网关未响应观战加入握手';
          action = 'SHOW_ERROR';
        } else if (resp.status === 'WATCH_TARGET_INVALID') {
          next = 'AUTO_JOIN_FAILED';
          nextCtx.failureReason = 'WATCH_TARGET_INVALID';
          nextCtx.failureMessage = resp.reason || '观战目标无效或房间已解散';
          action = 'SHOW_ERROR';
        }
      } else if (event.type === 'HAND_SNAPSHOT_RECEIVED') {
        // Snapshot received for target room
        if (nextCtx.activeTarget && nextCtx.activeTarget.roomId > 0 && nextCtx.activeTarget.roomId === event.roomId) {
          next = 'SPECTATING';
        }
      } else if (event.type === 'SPECTATOR_DISCONNECTED') {
        next = 'AUTO_JOIN_FAILED';
        nextCtx.failureReason = 'DISCONNECTED_DURING_JOIN';
        nextCtx.failureMessage = '加入观战过程中 观战运行时意外断开连接';
        nextCtx.spectatorConnectionId = null;
        action = 'SHOW_ERROR';
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 9. SPECTATING
    // -------------------------------------------------------------------------
    case 'SPECTATING': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'SPECTATOR_DISCONNECTED') {
        next = 'AUTO_JOIN_FAILED';
        nextCtx.failureReason = 'SPECTATOR_DISCONNECTED';
        nextCtx.failureMessage = '观战推流中断：观战运行时断开连接';
        nextCtx.spectatorConnectionId = null;
        action = 'SHOW_ERROR';
      } else if (event.type === 'HAND_SNAPSHOT_RECEIVED') {
        // Healthy streaming
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 10. AUTO_JOIN_FAILED
    // -------------------------------------------------------------------------
    case 'AUTO_JOIN_FAILED': {
      if (event.type === 'MAIN_MATCH_ENDED') {
        next = 'MAIN_MATCH_ENDED';
      } else if (event.type === 'RETRY_AUTO_JOIN') {
        nextCtx.retryCount += 1;
        nextCtx.failureReason = null;
        nextCtx.failureMessage = null;

        // Re-evaluate state
        const hasConn = !!nextCtx.spectatorConnectionId;
        const hasExpectedId = nextCtx.spectatorPlayerId === nextCtx.expectedSpectatorPlayerId;
        const hasPid = typeof nextCtx.spectatorPid === 'number' && nextCtx.spectatorPid > 0;

        if (hasConn && hasExpectedId) {
          next = 'SPECTATOR_READY';
          action = 'SEND_WATCH_TARGET';
        } else if (hasConn && nextCtx.spectatorPlayerId === null) {
          next = 'WAITING_SPECTATOR_LOGIN';
        } else if (hasPid) {
          next = 'WAITING_SPECTATOR_RUNTIME';
        } else {
          next = 'LAUNCHING_SPECTATOR_CLIENT';
          action = 'LAUNCH_CLIENT';
        }
      } else if (event.type === 'MANUAL_FALLBACK_CODE_ENTERED') {
        nextCtx.manualFallbackCode = event.watchCode.trim().toUpperCase();
        if (event.roomId) {
          nextCtx.activeTarget = {
            roomId: event.roomId,
            watchCode: nextCtx.manualFallbackCode,
          };
        } else if (nextCtx.activeTarget) {
          nextCtx.activeTarget.watchCode = nextCtx.manualFallbackCode;
        } else {
          nextCtx.activeTarget = {
            roomId: 0,
            watchCode: nextCtx.manualFallbackCode,
          };
        }

        nextCtx.failureReason = null;
        nextCtx.failureMessage = null;

        const hasConn = !!nextCtx.spectatorConnectionId;
        const hasExpectedId = nextCtx.spectatorPlayerId === nextCtx.expectedSpectatorPlayerId;
        if (hasConn && hasExpectedId) {
          next = 'AUTO_JOINING';
          action = 'SEND_WATCH_TARGET';
        } else {
          next = 'WAITING_SPECTATOR_RUNTIME';
        }
      } else if (event.type === 'RESET_TO_IDLE') {
        next = 'IDLE';
        nextCtx.activeTarget = null;
        nextCtx.failureReason = null;
        nextCtx.failureMessage = null;
      }
      break;
    }

    // -------------------------------------------------------------------------
    // 11. MAIN_MATCH_ENDED
    // -------------------------------------------------------------------------
    case 'MAIN_MATCH_ENDED': {
      if (event.type === 'RESET_TO_IDLE' || event.type === 'MAIN_MATCH_DETECTED') {
        nextCtx.activeTarget = null;
        nextCtx.failureReason = null;
        nextCtx.failureMessage = null;
        if (event.type === 'MAIN_MATCH_DETECTED') {
          nextCtx.activeTarget = { ...event.target };
          next = 'SPECTATOR_READY';
          action = 'SEND_WATCH_TARGET';
        } else {
          next = 'IDLE';
        }
      }
      break;
    }
  }

  // Readiness is evaluated when a real watch code arrives, including a late code or a new match.
  if (event.type === 'MAIN_MATCH_DETECTED' && ['IDLE', 'MAIN_IN_MATCH', 'MAIN_MATCH_ENDED', 'SPECTATOR_READY'].includes(current)) {
    const hasCode = !!(event.target.spectatorCode || event.target.watchCode);
    const hasConn = !!nextCtx.spectatorConnectionId;
    if (!hasCode) {
      next = 'MAIN_IN_MATCH';
      action = 'NONE';
    } else if (hasConn && nextCtx.spectatorPlayerId === nextCtx.expectedSpectatorPlayerId) {
      next = 'SPECTATOR_READY';
      action = 'SEND_WATCH_TARGET';
    } else if (hasConn && nextCtx.spectatorPlayerId === null) {
      next = 'WAITING_SPECTATOR_LOGIN';
      action = 'NONE';
    } else if (hasConn) {
      next = 'AUTO_JOIN_FAILED';
      nextCtx.failureReason = 'WRONG_ACCOUNT_LOGGED_IN';
      action = 'SHOW_ERROR';
    } else if (nextCtx.spectatorPid) {
      next = 'WAITING_SPECTATOR_RUNTIME';
      action = 'NONE';
    } else {
      next = 'SPECTATOR_CLIENT_OFFLINE';
      action = 'LAUNCH_CLIENT';
    }
  }
  if (event.type === 'RESET_TO_IDLE') {
    next = 'IDLE';
    nextCtx.activeTarget = null;
    nextCtx.lastRequestId = null;
    nextCtx.failureReason = null;
    nextCtx.failureMessage = null;
    action = 'NONE';
  }
  if (event.type === 'MAIN_MATCH_ENDED') {
    next = 'MAIN_MATCH_ENDED';
    nextCtx.activeTarget = null;
    nextCtx.lastRequestId = null;
    action = 'NONE';
  }
  if (event.type === 'SPECTATOR_DISCONNECTED') {
    nextCtx.spectatorConnectionId = null;
    nextCtx.spectatorPlayerId = null;
  }

  const changed = next !== current;
  if (changed) {
    nextCtx.previousState = current;
    nextCtx.currentState = next;
    nextCtx.stateTimestamp = nowMs;
    nextCtx.history = [
      ...nextCtx.history,
      {
        from: current,
        to: next,
        trigger: event.type,
        timestamp: nowMs,
      },
    ].slice(-50); // Retain latest 50 entries
  }

  return {
    previousState: current,
    nextState: next,
    changed,
    context: nextCtx,
    actionRequired: action,
    errorMessage: nextCtx.failureMessage ?? undefined,
  };
}

/**
 * Stateful wrapper for AutoFollowStateMachine.
 */
export class AutoFollowStateMachine {
  private context: AutoFollowContext;
  private listeners: Set<(ctx: AutoFollowContext, result: AutoFollowTransitionResult) => void> = new Set();

  constructor(initialOverrides?: Partial<AutoFollowContext>) {
    this.context = createInitialAutoFollowContext(initialOverrides);
  }

  public getContext(): AutoFollowContext {
    return { ...this.context };
  }

  public getState(): AutoFollowState {
    return this.context.currentState;
  }

  public configureBindings(mainPlayerId?: number | null, spectatorPlayerId?: number | null, spectatorPlatform?: string | null): void {
    if ((spectatorPlatform && spectatorPlatform !== this.context.targetPlatform) ||
        (spectatorPlayerId && spectatorPlayerId !== this.context.expectedSpectatorPlayerId)) {
      this.context.spectatorConnectionId = null;
      this.context.spectatorPlayerId = null;
      this.context.spectatorPid = null;
      this.context.currentState = 'IDLE';
    }
    if (spectatorPlatform) this.context.targetPlatform = spectatorPlatform;
    if (mainPlayerId && mainPlayerId > 0) this.context.mainPlayerId = mainPlayerId;
    if (spectatorPlayerId && spectatorPlayerId > 0) this.context.expectedSpectatorPlayerId = spectatorPlayerId;
  }

  public dispatch(event: AutoFollowEvent): AutoFollowTransitionResult {
    const result = processAutoFollowTransition(this.context, event);
    this.context = result.context;

    for (const listener of this.listeners) {
      try {
        listener(this.context, result);
      } catch (err) {
        console.error('AutoFollowStateMachine listener error:', err);
      }
    }

    return result;
  }

  public subscribe(
    listener: (ctx: AutoFollowContext, result: AutoFollowTransitionResult) => void
  ): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }
}

// ---------------------------------------------------------------------------
// UI Localization and Status Helpers
// ---------------------------------------------------------------------------

export const AUTO_FOLLOW_STATE_CONFIG_ZH: Record<
  AutoFollowState,
  { label: string; description: string; color: string; bg: string; border: string }
> = {
  IDLE: {
    label: '空闲待命',
    description: '主游戏账号处于大厅或离线，等待进入对局',
    color: '#94a3b8',
    bg: 'rgba(148, 163, 184, 0.12)',
    border: 'rgba(148, 163, 184, 0.25)',
  },
  MAIN_IN_MATCH: {
    label: '主账号对局已检测',
    description: '检测到主账号进入房间，正在准备观战链路',
    color: '#38bdf8',
    bg: 'rgba(56, 189, 248, 0.15)',
    border: 'rgba(56, 189, 248, 0.35)',
  },
  SPECTATOR_CLIENT_OFFLINE: {
    label: '观战客户端离线',
    description: '专用观战客户端尚未启动',
    color: '#fbbf24',
    bg: 'rgba(245, 158, 11, 0.15)',
    border: 'rgba(245, 158, 11, 0.35)',
  },
  LAUNCHING_SPECTATOR_CLIENT: {
    label: '正在拉起观战端',
    description: '星枢正在自动启动 已绑定观战客户端',
    color: '#818cf8',
    bg: 'rgba(129, 140, 248, 0.18)',
    border: 'rgba(129, 140, 248, 0.4)',
  },
  WAITING_SPECTATOR_RUNTIME: {
    label: '等待运行时连接',
    description: '观战端进程已启动，等待 APX 运行时接入 TCP:25890',
    color: '#a855f7',
    bg: 'rgba(168, 85, 247, 0.15)',
    border: 'rgba(168, 85, 247, 0.35)',
  },
  WAITING_SPECTATOR_LOGIN: {
    label: '等待账号登录识别',
    description: 'APX 运行时已连入，等待识别登录账号 3045259',
    color: '#06b6d4',
    bg: 'rgba(6, 182, 212, 0.15)',
    border: 'rgba(6, 182, 212, 0.35)',
  },
  SPECTATOR_READY: {
    label: '观战端已就绪',
    description: '已绑定观战端校验通过，准备发送自动跟随指令',
    color: '#4ade80',
    bg: 'rgba(74, 222, 128, 0.15)',
    border: 'rgba(74, 222, 128, 0.35)',
  },
  AUTO_JOINING: {
    label: '自动跟随加入中',
    description: '已向 观战客户端下发 WATCH_TARGET 指令，等待房间握手',
    color: '#fb923c',
    bg: 'rgba(251, 146, 60, 0.18)',
    border: 'rgba(251, 146, 60, 0.45)',
  },
  SPECTATING: {
    label: '全知观战进行中',
    description: '已成功连入对局，正在实时透视 4 名玩家手牌与战局数据',
    color: '#22c55e',
    bg: 'rgba(34, 197, 94, 0.2)',
    border: 'rgba(34, 197, 94, 0.5)',
  },
  AUTO_JOIN_FAILED: {
    label: '自动跟随异常',
    description: '自动加入观战失败，支持重试或手动输入观战码',
    color: '#ef4444',
    bg: 'rgba(239, 68, 68, 0.15)',
    border: 'rgba(239, 68, 68, 0.45)',
  },
  MAIN_MATCH_ENDED: {
    label: '主账号对局已结束',
    description: '主游戏账号对局已退出或结束，观战端已自动结算退房',
    color: '#94a3b8',
    bg: 'rgba(148, 163, 184, 0.12)',
    border: 'rgba(148, 163, 184, 0.25)',
  },
};
