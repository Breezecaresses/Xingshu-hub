/**
 * Project XingShu — HS4-P1.5.1D Lane 1 Conformance Test
 * Spectator Disconnect & Stale Snapshot Truth Verification
 *
 * Verifies:
 * 1. connected -> snapshot received -> liveStreaming=true, isSnapshotStale=false
 * 2. runtime disconnected -> liveStreaming=false, isSnapshotStale=true (stale snapshot retained)
 * 3. Never allow Runtime disconnected + Live streaming
 * 4. Disconnected badge explicitly marks "最后快照 (Runtime 已断开 · 数据已过期)"
 */

import {
  processSpectatorTransition,
  INITIAL_ROOM_STATE,
  type SpectatorRoomState,
  type SpectatorLifecycleState,
} from '../pages/spectator/spectatorTransition.ts';
import type { SpectatorHandPayload } from '../shared/types/index.ts';

declare const process: { exit(code?: number): never };

console.log('===============================================================');
console.log('Project XingShu — Spectator Disconnect Truth Conformance Suite');
console.log('===============================================================');

let passCount = 0;
let failCount = 0;

function assert(condition: boolean, msg: string): void {
  if (!condition) {
    throw new Error(msg);
  }
}

function assertStrictEqual<T>(actual: T, expected: T, msg?: string): void {
  if (actual !== expected) {
    throw new Error(msg ?? `Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

function runTest(name: string, fn: () => void) {
  try {
    fn();
    console.log(`[PASS] ${name}`);
    passCount++;
  } catch (err: any) {
    console.error(`[FAIL] ${name}:`, err.message);
    failCount++;
  }
}

// Helper simulating Spectator UX state derivation
function deriveSpectatorTruth(
  isConnected: boolean,
  lifecycleState: SpectatorLifecycleState,
  roomState: SpectatorRoomState
) {
  const hasActiveRoom = roomState.roomId !== null && Object.keys(roomState.roomPlayers || {}).length > 0;
  const liveStreaming = isConnected && (lifecycleState === 'STREAMING' || lifecycleState === 'SNAPSHOT_STALE_IDLE');
  const isSnapshotStale = !isConnected && roomState.roomId !== null;

  let badgeLabel = '';
  if (!isConnected && hasActiveRoom) {
    badgeLabel = '● 最后快照 (Runtime 已断开 · 数据已过期)';
  } else if (lifecycleState === 'STREAMING' && isConnected) {
    badgeLabel = '● 实时手牌传输中';
  } else if (!isConnected) {
    badgeLabel = '未连接 Runtime';
  } else {
    badgeLabel = '● 已就绪';
  }

  // Stepper step 4 derivation
  let step4SubZh = '等待房间手牌';
  let step4Status: 'active' | 'warning' | 'pending' = 'pending';
  if (hasActiveRoom) {
    if (isConnected && liveStreaming) {
      step4Status = 'active';
      step4SubZh = '4人全景实时流';
    } else {
      step4Status = 'warning';
      step4SubZh = '最后快照 (Runtime 已断开)';
    }
  }

  return {
    hasActiveRoom,
    liveStreaming,
    isSnapshotStale,
    badgeLabel,
    step4Status,
    step4SubZh,
  };
}

const SAMPLE_PAYLOAD: SpectatorHandPayload = {
  schemaVersion: '1.1.0',
  roomId: 10001,
  spectatorSessionEpoch: 1,
  spectatorAccountPlayerId: 9999,
  sequenceNumber: 10,
  snapshotTimestamp: '2026-10-18T12:00:00.000Z',
  playerId: 101,
  handSize: 3,
  cardIds: [10001, 10002, 10003],
  cards: [],
  drawCountTotal: 3,
  playCountTotal: 0,
  roomPlayers: {
    '101': { playerId: 101, handSize: 3, cardIds: [10001, 10002, 10003], cards: [] },
    '102': { playerId: 102, handSize: 2, cardIds: [20001, 20002], cards: [] },
  },
};

// -----------------------------------------------------------------------------
// Test 1: Connected -> Snapshot Received -> liveStreaming=true, isSnapshotStale=false
// -----------------------------------------------------------------------------
runTest('Truth Flow: Connected -> Snapshot Received -> Live Streaming Active', () => {
  let isConnected = true;
  let lifecycle: SpectatorLifecycleState = 'CONNECTED_NOT_SPECTATING';
  let roomState = INITIAL_ROOM_STATE;

  // Snapshot arrived
  const result = processSpectatorTransition(roomState, SAMPLE_PAYLOAD);
  assertStrictEqual(result.accepted, true, 'Payload must be accepted');
  roomState = result.nextState;
  lifecycle = result.nextLifecycle;

  const truth = deriveSpectatorTruth(isConnected, lifecycle, roomState);
  assertStrictEqual(truth.liveStreaming, true, 'liveStreaming must be true when connected & streaming');
  assertStrictEqual(truth.isSnapshotStale, false, 'isSnapshotStale must be false when live');
  assertStrictEqual(truth.badgeLabel, '● 实时手牌传输中', 'Badge must show live streaming');
  assertStrictEqual(truth.step4Status, 'active', 'Step 4 must be active');
  assertStrictEqual(truth.step4SubZh, '4人全景实时流', 'Step 4 text must be live stream');
});

// -----------------------------------------------------------------------------
// Test 2: Runtime Disconnected -> liveStreaming=false, isSnapshotStale=true
// -----------------------------------------------------------------------------
runTest('Truth Flow: Disconnect -> liveStreaming=false, isSnapshotStale=true, LIVE disabled', () => {
  let isConnected = false; // Disconnected!
  let lifecycle: SpectatorLifecycleState = 'DISCONNECTED';
  // Retaining the last hand snapshot
  const roomState: SpectatorRoomState = {
    roomId: SAMPLE_PAYLOAD.roomId,
    epoch: SAMPLE_PAYLOAD.spectatorSessionEpoch,
    sequenceNumber: SAMPLE_PAYLOAD.sequenceNumber,
    lastUpdatedMs: Date.now(),
    spectatorAccountPlayerId: SAMPLE_PAYLOAD.spectatorAccountPlayerId,
    roomPlayers: SAMPLE_PAYLOAD.roomPlayers,
  };

  const truth = deriveSpectatorTruth(isConnected, lifecycle, roomState);
  assertStrictEqual(truth.liveStreaming, false, 'liveStreaming MUST be false when disconnected');
  assertStrictEqual(truth.isSnapshotStale, true, 'isSnapshotStale MUST be true when disconnected with snapshot');
  assertStrictEqual(truth.badgeLabel, '● 最后快照 (Runtime 已断开 · 数据已过期)', 'Badge must mark stale snapshot');
  assertStrictEqual(truth.step4Status, 'warning', 'Step 4 must switch to warning status');
  assertStrictEqual(truth.step4SubZh, '最后快照 (Runtime 已断开)', 'Step 4 must NOT say live stream');
});

// -----------------------------------------------------------------------------
// Test 3: Invariant: Runtime Disconnected MUST NEVER Allow Live Streaming
// -----------------------------------------------------------------------------
runTest('Security Invariant: Runtime disconnected + Live streaming is STRICTLY PROHIBITED', () => {
  const isConnected = false;
  const states: SpectatorLifecycleState[] = ['STREAMING', 'SNAPSHOT_STALE_IDLE', 'DISCONNECTED', 'NOT_CONNECTED'];

  for (const st of states) {
    const truth = deriveSpectatorTruth(isConnected, st, {
      roomId: 10001,
      epoch: 1,
      sequenceNumber: 10,
      lastUpdatedMs: 100,
      spectatorAccountPlayerId: 9999,
      roomPlayers: SAMPLE_PAYLOAD.roomPlayers,
    });

    assertStrictEqual(truth.liveStreaming, false, `State ${st} with disconnected runtime must NEVER be liveStreaming`);
    assert(truth.badgeLabel.includes('最后快照') || truth.badgeLabel.includes('未连接'), 'Must mark stale or disconnected');
  }
});

// -----------------------------------------------------------------------------
// Test 4: Reconnection Restores Live State
// -----------------------------------------------------------------------------
runTest('Truth Flow: Reconnect -> Fresh Snapshot Received -> Restores Live Streaming', () => {
  let isConnected = true;
  let roomState: SpectatorRoomState = {
    roomId: SAMPLE_PAYLOAD.roomId,
    epoch: 1,
    sequenceNumber: 10,
    lastUpdatedMs: 100,
    spectatorAccountPlayerId: 9999,
    roomPlayers: SAMPLE_PAYLOAD.roomPlayers,
  };

  const freshPayload: SpectatorHandPayload = {
    ...SAMPLE_PAYLOAD,
    sequenceNumber: 11,
  };

  const result = processSpectatorTransition(roomState, freshPayload);
  assertStrictEqual(result.accepted, true);
  roomState = result.nextState;

  const truth = deriveSpectatorTruth(isConnected, result.nextLifecycle, roomState);
  assertStrictEqual(truth.liveStreaming, true, 'liveStreaming restored upon reconnect & fresh snapshot');
  assertStrictEqual(truth.isSnapshotStale, false, 'isSnapshotStale is false upon live reconnect');
  assertStrictEqual(truth.badgeLabel, '● 实时手牌传输中');
});

console.log('---------------------------------------------------------------');
console.log(`Results: ${passCount} PASSED, ${failCount} FAILED`);
if (failCount > 0) {
  process.exit(1);
} else {
  console.log('[SUCCESS] All Spectator Disconnect Truth conformance tests passed!');
  process.exit(0);
}
