/**
 * Project XingShu — Spectator UX State Transition Machine
 * Pure domain logic for evaluating incoming spectator hand payloads,
 * enforcing session epoch freshness, sequence monotonicity, and room transitions.
 * Pure TypeScript with ZERO React or Tauri runtime dependencies.
 */

import type { SpectatorHandPayload, PlayerHandSummary } from '../../shared/types/index.ts';

export type SpectatorLifecycleState =
  | 'NOT_CONNECTED'               // 1. Runtime / Bridge not connected via TCP
  | 'CONNECTED_NOT_SPECTATING'    // 2. TCP connected, but not currently spectating a room
  | 'SYNCHRONIZING'               // 3. Entered room, waiting for initial room snapshot
  | 'STREAMING'                   // 4. Actively receiving live spectator hand snapshots
  | 'SNAPSHOT_STALE_IDLE'         // 5. Normal match pause / idle, no events for >12s
  | 'SESSION_EPOCH_CHANGED'       // 6. Epoch advanced or room switch, state wiped
  | 'DISCONNECTED'                // 7. Connection lost or runtime closed
  | 'WATCH_EXIT';                 // 8. Player or watcher left room

export interface SpectatorRoomState {
  roomId: number | null;
  epoch: number;
  sequenceNumber: number;
  lastUpdatedMs: number | null;
  spectatorAccountPlayerId: number | null;
  roomPlayers: Record<string, PlayerHandSummary>;
}

export const INITIAL_ROOM_STATE: SpectatorRoomState = {
  roomId: null,
  epoch: 0,
  sequenceNumber: 0,
  lastUpdatedMs: null,
  spectatorAccountPlayerId: null,
  roomPlayers: {},
};

export interface TransitionResult {
  nextState: SpectatorRoomState;
  nextLifecycle: SpectatorLifecycleState;
  accepted: boolean;
  reason?: string;
  isNewEpochOrRoom: boolean;
}

/**
 * Pure state transition function evaluating incoming SpectatorHandPayload
 * against current SpectatorRoomState with strict Epoch and Sequence freshness gating.
 */
export function processSpectatorTransition(
  current: SpectatorRoomState,
  payload: SpectatorHandPayload,
  nowMs: number = Date.now()
): TransitionResult {
  // 1. Stale Epoch Defense: Reject events from past epochs
  if (payload.spectatorSessionEpoch < current.epoch) {
    return {
      nextState: current,
      nextLifecycle: 'STREAMING',
      accepted: false,
      reason: `Stale epoch event rejected: event epoch=${payload.spectatorSessionEpoch} < active epoch=${current.epoch}`,
      isNewEpochOrRoom: false,
    };
  }

  // 2. Epoch Advance or Room Transition: Clean wipe & reset
  const isNewEpoch = payload.spectatorSessionEpoch > current.epoch;
  const isNewRoom = current.roomId !== null && payload.roomId !== current.roomId;
  const isNewEpochOrRoom = isNewEpoch || isNewRoom;

  // 3. Monotonic Sequence Defense (within same epoch): Reject out-of-order sequence
  if (!isNewEpoch && payload.roomId === current.roomId) {
    if (payload.sequenceNumber <= current.sequenceNumber) {
      return {
        nextState: current,
        nextLifecycle: 'STREAMING',
        accepted: false,
        reason: `Out-of-order sequence rejected: seq=${payload.sequenceNumber} <= highest=${current.sequenceNumber}`,
        isNewEpochOrRoom: false,
      };
    }
  }

  // 4. Accept fresh payload
  const newRoomState: SpectatorRoomState = {
    roomId: payload.roomId,
    epoch: payload.spectatorSessionEpoch,
    sequenceNumber: payload.sequenceNumber,
    lastUpdatedMs: nowMs,
    spectatorAccountPlayerId: payload.spectatorAccountPlayerId || null,
    roomPlayers: payload.roomPlayers || {},
  };

  const hasPlayers = Object.keys(newRoomState.roomPlayers).length > 0;
  const nextLifecycle: SpectatorLifecycleState = hasPlayers
    ? 'STREAMING'
    : isNewEpochOrRoom
    ? 'SESSION_EPOCH_CHANGED'
    : 'SYNCHRONIZING';

  return {
    nextState: newRoomState,
    nextLifecycle,
    accepted: true,
    isNewEpochOrRoom,
  };
}

/**
 * Formats spectator player ID for UI display with truth guarantee.
 * Never leaks hardcoded test identity.
 *
 * Truth Rules:
 * - If playerId is a valid non-zero number, returns String(playerId)
 * - If playerId is null/undefined/0:
 *   - If game is running or runtime is connected: returns '等待 Runtime 识别' (zh) / 'Waiting for Runtime' (en)
 *   - If game is offline / disconnected: returns '未识别' (zh) / 'Unidentified' (en)
 */
export function formatSpectatorPlayerId(
  playerId: number | null | undefined,
  isConnectedOrRunning: boolean,
  isZh: boolean = true
): string {
  if (playerId !== null && playerId !== undefined && playerId !== 0) {
    return String(playerId);
  }
  return isConnectedOrRunning
    ? (isZh ? '等待 Runtime 识别' : 'Waiting for Runtime')
    : (isZh ? '未识别' : 'Unidentified');
}


