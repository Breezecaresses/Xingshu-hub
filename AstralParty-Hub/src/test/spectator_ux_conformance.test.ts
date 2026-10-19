/**
 * Project XingShu — HS4-P1 Spectator UX & Card Visual Conformance Suite
 * Verifies Card Visual Registry, Multiset Invariants, Fallbacks,
 * and Spectator UX State Machine (Epoch freshness, Sequence monotonicity, Room transitions).
 */

import {
  resolveCard,
  groupCardsForDisplay,
  getCardCategoryBadgeStyle,
} from '../shared/cardRegistry.ts';
import {
  processSpectatorTransition,
  type SpectatorRoomState,
  INITIAL_ROOM_STATE,
} from '../pages/spectator/spectatorTransition.ts';
import type { SpectatorHandPayload } from '../shared/types/index.ts';

declare const process: { exit(code?: number): never };

console.log('===============================================================');
console.log('Project XingShu — HS4-P1 Spectator UX & Card Conformance Suite');
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

function assertDeepStrictEqual<T>(actual: T, expected: T, msg?: string): void {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(msg ?? `Deep equal mismatch:\nActual: ${JSON.stringify(actual)}\nExpected: ${JSON.stringify(expected)}`);
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

// -------------------------------------------------------------
// Test Group 1: Card Visual Registry & Fallbacks
// -------------------------------------------------------------
runTest('Card Registry — Known basic cards resolve correctly', () => {
  const c10001 = resolveCard(10001);
  assertStrictEqual(c10001.cardId, 10001);
  assertStrictEqual(c10001.name, '攻击(中)');
  assertStrictEqual(c10001.category, '攻击牌');
  assertStrictEqual(c10001.cost, 1);
  assertStrictEqual(c10001.icon, '/cards/UT_HandCard_10001.png');

  const c10004 = resolveCard(10004);
  assertStrictEqual(c10004.cardId, 10004);
  assertStrictEqual(c10004.name, '防御(大)');
  assertStrictEqual(c10004.category, '防御牌');
  assertStrictEqual(c10004.cost, 1);

  const c20005 = resolveCard(20005);
  assertStrictEqual(c20005.cardId, 20005);
  assertStrictEqual(c20005.name, '遥控骰子');
  assertStrictEqual(c20005.category, '霉运牌');
});

runTest('Card Registry — Unknown cards fallback gracefully without crashing', () => {
  const unknownCard = resolveCard(99999);
  assertStrictEqual(unknownCard.cardId, 99999);
  assertStrictEqual(unknownCard.name, '未知卡牌');
  assertStrictEqual(unknownCard.category, '未知');
  assertStrictEqual(unknownCard.icon, '/cards/card_fallback.svg');
});

runTest('Card Registry — Category badge styling returns valid color tokens', () => {
  const atkStyle = getCardCategoryBadgeStyle('攻击牌');
  assert(atkStyle.bg.includes('239, 68, 68'), 'Attack card badge must have red token');
  const defStyle = getCardCategoryBadgeStyle('防御牌');
  assert(defStyle.bg.includes('59, 130, 246'), 'Defense card badge must have blue token');
  const effStyle = getCardCategoryBadgeStyle('效果牌');
  assert(effStyle.bg.includes('168, 85, 247'), 'Effect card badge must have purple token');
});

// -------------------------------------------------------------
// Test Group 2: Multiset Grouping & Card Quantity Preservation
// -------------------------------------------------------------
runTest('Multiset Grouping — Duplicate cards collapsed into count with invariant preserved', () => {
  const cardIds = [10004, 10002, 10004, 10004, 10003]; // 3x 10004, 1x 10002, 1x 10003
  const grouped = groupCardsForDisplay(cardIds);

  assertStrictEqual(grouped.length, 3, 'Expected 3 distinct card groups');

  const c10004Group = grouped.find((g) => g.cardId === 10004);
  assert(!!c10004Group, 'Card 10004 must be present in grouped results');
  assertStrictEqual(c10004Group?.count, 3, 'Expected count 3 for card 10004');

  const c10002Group = grouped.find((g) => g.cardId === 10002);
  assert(!!c10002Group, 'Card 10002 must be present in grouped results');
  assertStrictEqual(c10002Group?.count, 1, 'Expected count 1 for card 10002');

  // Strict Invariant: Total count sum MUST equal cardIds.length
  const totalCount = grouped.reduce((sum, item) => sum + item.count, 0);
  assertStrictEqual(totalCount, cardIds.length, 'Total grouped count must strictly equal input array length');
});

runTest('Multiset Grouping — Empty array returns empty without errors', () => {
  const grouped = groupCardsForDisplay([]);
  assertDeepStrictEqual(grouped, []);
});

// -------------------------------------------------------------
// Test Group 3: Spectator UX State Machine Transitions
// -------------------------------------------------------------
const HS3_3_2_ROOM_665945_SEQ11: SpectatorHandPayload = {
  schemaVersion: '1.1.0',
  roomId: 665945,
  spectatorSessionEpoch: 1,
  spectatorAccountPlayerId: 1556514,
  sequenceNumber: 11,
  snapshotTimestamp: '2026-10-16T20:55:00.000Z',
  playerId: 105413,
  handSize: 5,
  cardIds: [20010, 10002, 10004, 10004, 10003],
  cards: [
    { cardId: 20010, count: 1 },
    { cardId: 10002, count: 1 },
    { cardId: 10004, count: 2 },
    { cardId: 10003, count: 1 },
  ],
  drawCountTotal: 5,
  playCountTotal: 0,
  roomPlayers: {
    '906124': {
      playerId: 906124,
      handSize: 7,
      cardIds: [10004, 10002, 20027, 20029, 10008, 10001, 20011],
      cards: [],
    },
    '1223091': {
      playerId: 1223091,
      handSize: 2,
      cardIds: [10002, 10004],
      cards: [],
    },
    '105413': {
      playerId: 105413,
      handSize: 5,
      cardIds: [20010, 10002, 10004, 10004, 10003],
      cards: [],
    },
    '151220': {
      playerId: 151220,
      handSize: 5,
      cardIds: [20005, 10008, 20010, 21012, 21006],
      cards: [],
    },
  },
};

runTest('UX State Machine — Initial payload transitions to STREAMING', () => {
  const result = processSpectatorTransition(INITIAL_ROOM_STATE, HS3_3_2_ROOM_665945_SEQ11, 1000);
  assertStrictEqual(result.accepted, true);
  assertStrictEqual(result.nextLifecycle, 'STREAMING');
  assertStrictEqual(result.nextState.roomId, 665945);
  assertStrictEqual(result.nextState.epoch, 1);
  assertStrictEqual(result.nextState.sequenceNumber, 11);
  assertStrictEqual(Object.keys(result.nextState.roomPlayers).length, 4);
});

runTest('UX State Machine — Stale Epoch event rejected fail-closed', () => {
  const currentState: SpectatorRoomState = {
    ...INITIAL_ROOM_STATE,
    roomId: 665945,
    epoch: 2, // Active epoch is 2
    sequenceNumber: 5,
  };

  const stalePayload: SpectatorHandPayload = {
    ...HS3_3_2_ROOM_665945_SEQ11,
    spectatorSessionEpoch: 1, // Stale epoch 1 < 2
    sequenceNumber: 20,
  };

  const result = processSpectatorTransition(currentState, stalePayload, 2000);
  assertStrictEqual(result.accepted, false);
  assert(result.reason?.includes('Stale epoch event rejected') ?? false, 'Must reject stale epoch');
  assertStrictEqual(result.nextState.epoch, 2);
  assertStrictEqual(result.nextState.sequenceNumber, 5);
});

runTest('UX State Machine — Out-of-order sequence rejected within same epoch', () => {
  const currentState: SpectatorRoomState = {
    ...INITIAL_ROOM_STATE,
    roomId: 665945,
    epoch: 1,
    sequenceNumber: 15, // Current highest seq is 15
  };

  const outOfOrderPayload: SpectatorHandPayload = {
    ...HS3_3_2_ROOM_665945_SEQ11,
    spectatorSessionEpoch: 1,
    sequenceNumber: 14, // 14 <= 15
  };

  const result = processSpectatorTransition(currentState, outOfOrderPayload, 3000);
  assertStrictEqual(result.accepted, false);
  assert(result.reason?.includes('Out-of-order sequence rejected') ?? false, 'Must reject out of order seq');
});

runTest('UX State Machine — Equal duplicate sequence rejected', () => {
  const currentState: SpectatorRoomState = {
    ...INITIAL_ROOM_STATE,
    roomId: 665945,
    epoch: 1,
    sequenceNumber: 15,
  };

  const dupPayload: SpectatorHandPayload = {
    ...HS3_3_2_ROOM_665945_SEQ11,
    spectatorSessionEpoch: 1,
    sequenceNumber: 15, // 15 <= 15
  };

  const result = processSpectatorTransition(currentState, dupPayload, 3500);
  assertStrictEqual(result.accepted, false);
});

runTest('UX State Machine — Monotonic sequence advance accepted', () => {
  const currentState: SpectatorRoomState = {
    ...INITIAL_ROOM_STATE,
    roomId: 665945,
    epoch: 1,
    sequenceNumber: 11,
  };

  const freshPayload: SpectatorHandPayload = {
    ...HS3_3_2_ROOM_665945_SEQ11,
    sequenceNumber: 12,
  };

  const result = processSpectatorTransition(currentState, freshPayload, 4000);
  assertStrictEqual(result.accepted, true);
  assertStrictEqual(result.nextState.sequenceNumber, 12);
  assertStrictEqual(result.isNewEpochOrRoom, false);
});

runTest('UX State Machine — Epoch Advance triggers clean state transition', () => {
  const currentState: SpectatorRoomState = {
    ...INITIAL_ROOM_STATE,
    roomId: 665945,
    epoch: 1,
    sequenceNumber: 99,
  };

  const newEpochPayload: SpectatorHandPayload = {
    ...HS3_3_2_ROOM_665945_SEQ11,
    spectatorSessionEpoch: 2,
    sequenceNumber: 1, // Seq resets to 1 in new epoch
  };

  const result = processSpectatorTransition(currentState, newEpochPayload, 5000);
  assertStrictEqual(result.accepted, true);
  assertStrictEqual(result.isNewEpochOrRoom, true);
  assertStrictEqual(result.nextState.epoch, 2);
  assertStrictEqual(result.nextState.sequenceNumber, 1);
});

runTest('UX State Machine — 4-player HS3.3.2 Room 665945 data integrity', () => {
  const payload = HS3_3_2_ROOM_665945_SEQ11;
  const pIds = Object.keys(payload.roomPlayers);
  assertStrictEqual(pIds.length, 4, 'Must contain exactly 4 remote players');
  assert(pIds.includes('906124'), 'Must contain player 906124');
  assert(pIds.includes('1223091'), 'Must contain player 1223091');
  assert(pIds.includes('105413'), 'Must contain player 105413');
  assert(pIds.includes('151220'), 'Must contain player 151220');

  // Verify spectator own account is 1556514 and is excluded from remote player hands
  assertStrictEqual(payload.spectatorAccountPlayerId, 1556514);
  assertStrictEqual(pIds.includes('1556514'), false, 'Spectator self account must not be in roomPlayers');

  // Verify handSize == len(cardIds) invariant for all 4 players
  for (const pid of pIds) {
    const player = payload.roomPlayers[pid];
    assertStrictEqual(
      player.handSize,
      player.cardIds.length,
      `Player ${pid} handSize ${player.handSize} must equal cardIds.length ${player.cardIds.length}`
    );
  }
});

console.log('---------------------------------------------------------------');
console.log(`Results: ${passCount} PASSED, ${failCount} FAILED`);
if (failCount > 0) {
  process.exit(1);
} else {
  console.log('[SUCCESS] All Spectator UX and Card Conformance tests passed!');
  process.exit(0);
}
