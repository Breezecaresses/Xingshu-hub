/**
 * Project XingShu — HS4-P1.6 Comprehensive Identity Regression Test Suite
 * Lane: Identity & Account Binding Isolation
 *
 * Verifies:
 * 1. Strict Identity Rule: Local spectator identity strictly sourced from spectatorAccountPlayerId (3045259).
 *    Four watched remote players rotating payload.playerId (212667, 958038, 151220, 1946346) NEVER contaminate local identity.
 * 2. Reconnect, room change, epoch advance, and Runtime restart resilience.
 * 3. Simultaneous Steam MAIN (1556514, STEAM_CN) + TapTap USER_SPECTATOR (3045259, TAPTAP) dual-session coexistence and mutual isolation.
 * 4. Explicit account bindings retain the user's chosen ID and platform; remote snapshots never rewrite them.
 * 5. Unknown accounts resolve to UNBOUND_ACCOUNT with zero automatic binding.
 * 6. Zero credential storage and security whitelist audit.
 */

import assert from 'node:assert';
import {
  GameAccountService,
  MemoryStorage,
} from '../services/gameAccountService.ts';
import {
  GameAccountRole,
  ResolvedAccountRole,
  STORAGE_KEY_GAME_ACCOUNT_BINDINGS,
  FORBIDDEN_SECURITY_KEYS,
} from '../types/gameAccount.ts';
import {
  UserOwnedSpectatorAccountService,
} from '../services/spectatorAccountService.ts';
import {
  SpectatorAccountStatus,
} from '../types/spectatorAccount.ts';
import {
  processSpectatorTransition,
  INITIAL_ROOM_STATE,
  formatSpectatorPlayerId,
  type SpectatorRoomState,
} from '../pages/spectator/spectatorTransition.ts';
import {
  RuntimeSessionResolver,
} from '../services/runtimeSessionResolver.ts';
import type {
  RuntimeSession,
} from '../types/runtimeSession.ts';
import type {
  SpectatorHandPayload,
} from '../shared/types/index.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('='.repeat(80));
console.log('PROJECT XINGSHU HS4-P1.6: IDENTITY & ACCOUNT BINDING ISOLATION REGRESSION SUITE');
console.log('='.repeat(80));

let passCount = 0;
let failCount = 0;

function runTest(name: string, fn: () => void): void {
  try {
    fn();
    console.log(`[PASS] ${name}`);
    passCount++;
  } catch (err: any) {
    console.error(`[FAIL] ${name}: ${err.message}`);
    failCount++;
    throw err;
  }
}

// =============================================================================
// SUITE 1: STRICT IDENTITY RULE & FOUR-PLAYER ROTATION CONTAMINATION IMMUNITY
// =============================================================================
console.log('\n--- SUITE 1: Four Remote Players Rotating payload.playerId Immunity ---');

runTest('Suite 1.1: Spectator UID 3045259 immune to watched player 212667 (previously contaminating ID)', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);
  const specAccountSvc = new UserOwnedSpectatorAccountService(storage);

  // Explicit binding: USER_SPECTATOR = 3045259 on TAPTAP
  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '专用观战账号');

  // Incoming payload for remote Player 1 (212667)
  const payload1: SpectatorHandPayload = {
    schemaVersion: '1.1.0',
    roomId: 152953,
    spectatorSessionEpoch: 1,
    spectatorAccountPlayerId: 3045259, // Authentic local spectator account
    sequenceNumber: 1,
    snapshotTimestamp: '2026-10-18T14:00:00Z',
    playerId: 212667,                 // Watched remote player whose hand is serialized
    cardIds: [10001, 10003],
    cards: [{ cardId: 10001, count: 1 }, { cardId: 10003, count: 1 }],
    handSize: 2,
    drawCountTotal: 4,
    playCountTotal: 2,
    roomPlayers: {
      '212667': { playerId: 212667, handSize: 2, cardIds: [10001, 10003], cards: [], drawCountTotal: 4, playCountTotal: 2 },
    },
  };

  // 1. Transition state machine captures spectatorAccountPlayerId, NOT playerId
  const trans1 = processSpectatorTransition(INITIAL_ROOM_STATE, payload1);
  assert.strictEqual(trans1.accepted, true);
  assert.strictEqual(trans1.nextState.spectatorAccountPlayerId, 3045259, 'spectatorAccountPlayerId must be 3045259');
  assert.notStrictEqual(trans1.nextState.spectatorAccountPlayerId, 212667, 'MUST NEVER be 212667');

  // 2. Spectator account service syncs with authentic spectatorAccountPlayerId
  const specInfo1 = specAccountSvc.syncAccountInfo({
    gameRunning: true,
    runtimeConnected: true,
    spectatorAccountPlayerId: payload1.spectatorAccountPlayerId,
    roomId: payload1.roomId,
  });
  assert.strictEqual(specInfo1.currentSessionPlayerId, 3045259, 'currentSessionPlayerId strictly 3045259');
  assert.notStrictEqual(specInfo1.currentSessionPlayerId, 212667, 'MUST NOT be contaminated by remote player 212667');
  assert.strictEqual(specInfo1.status, SpectatorAccountStatus.IN_USE);

  // 3. UI format evaluates truthfully
  const uiText1 = formatSpectatorPlayerId(specInfo1.currentSessionPlayerId, true, true);
  assert.strictEqual(uiText1, '3045259', 'UI displays spectator UID 3045259');
  assert.notStrictEqual(uiText1, '212667', 'UI NEVER displays watched player 212667');

  // 4. Game account identity resolution
  const identity1 = gameAccountSvc.resolveSession(specInfo1.currentSessionPlayerId, 'TAPTAP', true);
  assert.strictEqual(identity1.resolvedRole, GameAccountRole.USER_SPECTATOR);
  assert.strictEqual(identity1.currentSessionPlayerId, 3045259);
  assert.strictEqual(identity1.binding?.platformId, 'TAPTAP');
});

runTest('Suite 1.2: Four players rotating turns (212667 -> 958038 -> 151220 -> 1946346) keeps spectator identity at 3045259', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);
  const specAccountSvc = new UserOwnedSpectatorAccountService(storage);

  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '专用观战账号');

  const remotePlayerIds = [212667, 958038, 151220, 1946346];
  let currentState: SpectatorRoomState = INITIAL_ROOM_STATE;

  for (let turn = 0; turn < remotePlayerIds.length; turn++) {
    const remotePid = remotePlayerIds[turn];
    const seq = turn + 1;

    const payload: SpectatorHandPayload = {
      schemaVersion: '1.1.0',
      roomId: 152953,
      spectatorSessionEpoch: 1,
      spectatorAccountPlayerId: 3045259,
      sequenceNumber: seq,
      snapshotTimestamp: new Date().toISOString(),
      playerId: remotePid, // Rotates across 4 room players
      cardIds: [10001 + turn],
      cards: [{ cardId: 10001 + turn, count: 1 }],
      handSize: 1,
      drawCountTotal: seq * 2,
      playCountTotal: seq,
      roomPlayers: {
        [String(remotePid)]: { playerId: remotePid, handSize: 1, cardIds: [10001 + turn], cards: [], drawCountTotal: seq * 2, playCountTotal: seq },
      },
    };

    const trans = processSpectatorTransition(currentState, payload);
    assert.strictEqual(trans.accepted, true, `Transition turn ${turn + 1} accepted`);
    currentState = trans.nextState;

    // Verify room state retains 3045259 on every turn
    assert.strictEqual(currentState.spectatorAccountPlayerId, 3045259, `Turn ${turn + 1}: room state spectator ID is 3045259`);
    assert.notStrictEqual(currentState.spectatorAccountPlayerId, remotePid, `Turn ${turn + 1}: must not equal remote ID ${remotePid}`);

    // Verify spectator service retains 3045259 on every turn
    const specInfo = specAccountSvc.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: payload.spectatorAccountPlayerId,
      roomId: payload.roomId,
    });
    assert.strictEqual(specInfo.currentSessionPlayerId, 3045259, `Turn ${turn + 1}: currentSessionPlayerId is 3045259`);
    assert.strictEqual(specInfo.playerId, 3045259);
    assert.notStrictEqual(specInfo.currentSessionPlayerId, remotePid, `Turn ${turn + 1}: must not equal remote ID ${remotePid}`);

    // Verify identity resolver resolves strictly to USER_SPECTATOR with 3045259
    const identity = gameAccountSvc.resolveSession(specInfo.currentSessionPlayerId, 'TAPTAP', true);
    assert.strictEqual(identity.resolvedRole, GameAccountRole.USER_SPECTATOR, `Turn ${turn + 1}: role is USER_SPECTATOR`);
    assert.strictEqual(identity.currentSessionPlayerId, 3045259);
  }
});

runTest('Suite 1.3: Direct negative test: watched remote players MUST NEVER resolve as spectator account', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);

  gameAccountSvc.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', '主账号');
  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '专用观战账号');

  // If buggy code mistakenly passed payload.playerId (212667) to resolveSession:
  const contaminatedIdentity = gameAccountSvc.resolveSession(212667, 'TAPTAP', true);
  assert.strictEqual(contaminatedIdentity.resolvedRole, ResolvedAccountRole.UNBOUND_ACCOUNT, '212667 is UNBOUND, cannot be USER_SPECTATOR');
  assert.strictEqual(contaminatedIdentity.binding, null, '212667 has null binding');

  // Observing a remote player cannot rewrite the binding. An explicit user
  // rebind is allowed for any valid account, without a developer-ID denylist.
  assert.strictEqual(gameAccountSvc.getBindingByRole(GameAccountRole.USER_SPECTATOR)?.playerId, 3045259);
  gameAccountSvc.bindAccount(212667, GameAccountRole.USER_SPECTATOR, 'TAPTAP', 'Explicit user choice');
  assert.strictEqual(gameAccountSvc.getBindingByRole(GameAccountRole.USER_SPECTATOR)?.playerId, 212667);
  assert.strictEqual(gameAccountSvc.getBindingByRole(GameAccountRole.MAIN)?.playerId, 1556514);
});

// =============================================================================
// SUITE 2: RECONNECT, ROOM CHANGE, EPOCH ADVANCE, RUNTIME RESTART RESILIENCE
// =============================================================================
console.log('\n--- SUITE 2: Reconnect, Room Change, Epoch Advance, Runtime Restart ---');

runTest('Suite 2.1: Room transition and epoch advance cleans room state while preserving spectator identity 3045259', () => {
  let roomState: SpectatorRoomState = INITIAL_ROOM_STATE;

  // Room 1, Epoch 1
  const payloadRoom1: SpectatorHandPayload = {
    schemaVersion: '1.1.0',
    roomId: 152953,
    spectatorSessionEpoch: 1,
    spectatorAccountPlayerId: 3045259,
    sequenceNumber: 10,
    snapshotTimestamp: '2026-10-18T14:05:00Z',
    playerId: 212667,
    cardIds: [10001, 10002],
    cards: [],
    handSize: 2,
    drawCountTotal: 4,
    playCountTotal: 2,
    roomPlayers: {
      '212667': { playerId: 212667, handSize: 2, cardIds: [10001, 10002], cards: [], drawCountTotal: 4, playCountTotal: 2 },
    },
  };
  const res1 = processSpectatorTransition(roomState, payloadRoom1);
  assert.strictEqual(res1.accepted, true);
  roomState = res1.nextState;
  assert.strictEqual(roomState.roomId, 152953);
  assert.strictEqual(roomState.epoch, 1);
  assert.strictEqual(roomState.spectatorAccountPlayerId, 3045259);

  // Transition to Room 2, Epoch 2 (New Match)
  const payloadRoom2: SpectatorHandPayload = {
    schemaVersion: '1.1.0',
    roomId: 998877,
    spectatorSessionEpoch: 2,
    spectatorAccountPlayerId: 3045259,
    sequenceNumber: 1,
    snapshotTimestamp: '2026-10-18T14:10:00Z',
    playerId: 958038,
    cardIds: [21001],
    cards: [],
    handSize: 1,
    drawCountTotal: 1,
    playCountTotal: 0,
    roomPlayers: {
      '958038': { playerId: 958038, handSize: 1, cardIds: [21001], cards: [], drawCountTotal: 1, playCountTotal: 0 },
    },
  };
  const res2 = processSpectatorTransition(roomState, payloadRoom2);
  assert.strictEqual(res2.accepted, true);
  assert.strictEqual(res2.isNewEpochOrRoom, true, 'isNewEpochOrRoom must be true');
  roomState = res2.nextState;

  // Invariants:
  assert.strictEqual(roomState.roomId, 998877, 'New room ID 998877 active');
  assert.strictEqual(roomState.epoch, 2, 'Epoch advanced to 2');
  assert.strictEqual(roomState.sequenceNumber, 1, 'Sequence reset to 1');
  assert.strictEqual(roomState.spectatorAccountPlayerId, 3045259, 'Spectator UID remains 3045259');
  assert.strictEqual(Object.keys(roomState.roomPlayers).length, 1, 'Old room players completely wiped');
  assert.strictEqual(roomState.roomPlayers['212667'], undefined, 'Old player 212667 absent from new room');
});

runTest('Suite 2.2: Disconnect resets live identity to null, reconnect restores 3045259 truthfully', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);
  const specAccountSvc = new UserOwnedSpectatorAccountService(storage);

  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '专用观战账号');

  // Step 1: Active streaming session
  specAccountSvc.syncAccountInfo({
    gameRunning: true,
    runtimeConnected: true,
    spectatorAccountPlayerId: 3045259,
    roomId: 152953,
  });
  assert.strictEqual(specAccountSvc.getAccountInfo().status, SpectatorAccountStatus.IN_USE);
  assert.strictEqual(specAccountSvc.getAccountInfo().currentSessionPlayerId, 3045259);

  // Step 2: Runtime disconnects
  specAccountSvc.resetSession();
  const disconnectedInfo = specAccountSvc.getAccountInfo();
  assert.strictEqual(disconnectedInfo.currentSessionPlayerId, null, 'currentSessionPlayerId must be null upon disconnect');
  assert.strictEqual(disconnectedInfo.playerId, null, 'playerId must be null upon disconnect');
  assert.strictEqual(disconnectedInfo.status, SpectatorAccountStatus.OFFLINE);
  assert.strictEqual(disconnectedInfo.lastKnownPlayerId, 3045259, 'lastKnownPlayerId preserved in persistence');

  // Resolving session when disconnected
  const discIdentity = gameAccountSvc.resolveSession(null, 'TAPTAP', false);
  assert.strictEqual(discIdentity.resolvedRole, ResolvedAccountRole.DISCONNECTED);
  assert.strictEqual(discIdentity.currentSessionPlayerId, null);

  // Step 3: Runtime reconnects in Main Menu (pre-packet)
  const reconnectingInfo = specAccountSvc.syncAccountInfo({
    gameRunning: true,
    runtimeConnected: true,
    spectatorAccountPlayerId: null,
    roomId: null,
  });
  assert.strictEqual(reconnectingInfo.status, SpectatorAccountStatus.IDENTITY_PENDING);
  assert.strictEqual(reconnectingInfo.currentSessionPlayerId, null, 'Must NOT promote lastKnownPlayerId pre-packet');

  const pendingIdentity = gameAccountSvc.resolveSession(null, 'TAPTAP', true);
  assert.strictEqual(pendingIdentity.resolvedRole, ResolvedAccountRole.CONNECTED_PENDING_IDENTITY);
  assert.notStrictEqual(pendingIdentity.resolvedRole, ResolvedAccountRole.DISCONNECTED);

  // Step 4: First spectator snapshot arrives after reconnect
  const reconnectedInfo = specAccountSvc.syncAccountInfo({
    gameRunning: true,
    runtimeConnected: true,
    spectatorAccountPlayerId: 3045259,
    roomId: 223344,
  });
  assert.strictEqual(reconnectedInfo.status, SpectatorAccountStatus.IN_USE);
  assert.strictEqual(reconnectedInfo.currentSessionPlayerId, 3045259);

  const reconnectedIdentity = gameAccountSvc.resolveSession(reconnectedInfo.currentSessionPlayerId, 'TAPTAP', true);
  assert.strictEqual(reconnectedIdentity.resolvedRole, GameAccountRole.USER_SPECTATOR);
  assert.strictEqual(reconnectedIdentity.currentSessionPlayerId, 3045259);
});

runTest('Suite 2.3: Runtime restart with new PID and connectionId recovers truthfully', () => {
  const resolver = new RuntimeSessionResolver([
    {
      bindingId: 'bind-tap-spec',
      platformId: 'TAPTAP',
      playerId: 3045259,
      displayName: '专用观战账号',
      role: GameAccountRole.USER_SPECTATOR,
      boundAt: '2026-10-18T00:00:00Z',
      lastSeenAt: null,
    },
  ]);

  // Old process crashes/stops
  const oldSessions: RuntimeSession[] = [
    { connectionId: 'conn-tap-old', platformId: 'TAPTAP', processId: 28624, playerId: 3045259, status: 'DISCONNECTED' },
  ];
  const oldResult = resolver.resolve(oldSessions, 'USER_SPECTATOR');
  assert.strictEqual(oldResult.selectedSpectatorSession, null);
  assert.strictEqual(oldResult.isSpectatorReady, false);

  // New process starts with new PID 49120 and new connectionId
  const newSessions: RuntimeSession[] = [
    { connectionId: 'conn-tap-new', platformId: 'TAPTAP', processId: 49120, playerId: 3045259, status: 'CONNECTED' },
  ];
  const newResult = resolver.resolve(newSessions, 'USER_SPECTATOR');
  assert.strictEqual(newResult.selectedSpectatorSession?.connectionId, 'conn-tap-new');
  assert.strictEqual(newResult.selectedSpectatorSession?.processId, 49120);
  assert.strictEqual(newResult.selectedSpectatorSession?.playerId, 3045259);
  assert.strictEqual(newResult.selectedSpectatorSession?.role, 'USER_SPECTATOR');
  assert.strictEqual(newResult.isSpectatorReady, true);
});

// =============================================================================
// SUITE 3: SIMULTANEOUS STEAM MAIN + TAPTAP SPECTATOR COEXISTENCE AND ISOLATION
// =============================================================================
console.log('\n--- SUITE 3: Dual Steam MAIN + TapTap Spectator Coexistence and Isolation ---');

runTest('Suite 3.1: Dual runtimes coexist with 2-client badge and strict per-platform identity isolation', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);

  // Configure canonical bindings
  gameAccountSvc.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', '我的主账号');
  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '专用观战账号');

  const resolver = new RuntimeSessionResolver(gameAccountSvc.getBindings());

  const activeSessions: RuntimeSession[] = [
    {
      connectionId: 'conn-steam-live',
      platformId: 'STEAM_CN',
      processId: 33548,
      playerId: 1556514,
      displayName: '我的主账号',
      status: 'CONNECTED',
    },
    {
      connectionId: 'conn-taptap-live',
      platformId: 'TAPTAP',
      processId: 28624,
      playerId: 3045259,
      displayName: '专用观战账号',
      status: 'CONNECTED',
    },
  ];

  // 1. Verify connected count & badge
  const connectedCount = activeSessions.filter((s) => s.status !== 'DISCONNECTED').length;
  assert.strictEqual(connectedCount, 2, '2 runtimes connected');
  const badge = connectedCount >= 2 ? '2 Runtime（双端在线）' : `${connectedCount} Runtime 已连接`;
  assert.strictEqual(badge, '2 Runtime（双端在线）');

  // 2. Resolve multi-client sessions
  const result = resolver.resolve(activeSessions, 'USER_SPECTATOR');
  assert.strictEqual(result.allSessions.length, 2);

  // Main session resolved correctly
  assert.strictEqual(result.mainSession?.connectionId, 'conn-steam-live');
  assert.strictEqual(result.mainSession?.platformId, 'STEAM_CN');
  assert.strictEqual(result.mainSession?.playerId, 1556514);
  assert.strictEqual(result.mainSession?.role, 'MAIN');

  // Spectator session resolved correctly
  assert.strictEqual(result.spectatorSession?.connectionId, 'conn-taptap-live');
  assert.strictEqual(result.spectatorSession?.platformId, 'TAPTAP');
  assert.strictEqual(result.spectatorSession?.playerId, 3045259);
  assert.strictEqual(result.spectatorSession?.role, 'USER_SPECTATOR');

  // Active spectator execution session MUST be TapTap, NEVER Steam CN!
  assert.strictEqual(result.selectedSpectatorSession?.connectionId, 'conn-taptap-live');
  assert.strictEqual(result.selectedSpectatorSession?.platformId, 'TAPTAP');
  assert.strictEqual(result.selectedSpectatorSession?.isSpectatorExecutionRuntime, true);
  assert.strictEqual(result.mainSession?.isSpectatorExecutionRuntime, false);

  // 3. Isolated session resolution in GameAccountService
  const steamIdentity = gameAccountSvc.resolveSession(1556514, 'STEAM_CN', true);
  const taptapIdentity = gameAccountSvc.resolveSession(3045259, 'TAPTAP', true);

  assert.strictEqual(steamIdentity.resolvedRole, GameAccountRole.MAIN);
  assert.strictEqual(steamIdentity.platformId, 'STEAM_CN');
  assert.strictEqual(steamIdentity.currentSessionPlayerId, 1556514);

  assert.strictEqual(taptapIdentity.resolvedRole, GameAccountRole.USER_SPECTATOR);
  assert.strictEqual(taptapIdentity.platformId, 'TAPTAP');
  assert.strictEqual(taptapIdentity.currentSessionPlayerId, 3045259);
});

runTest('Suite 3.2: TapTap spectator disconnects -> Steam MAIN remains completely active and uncorrupted', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);

  gameAccountSvc.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', '我的主账号');
  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '专用观战账号');

  const resolver = new RuntimeSessionResolver(gameAccountSvc.getBindings());

  // TapTap closes, only Steam CN remains
  const sessions: RuntimeSession[] = [
    {
      connectionId: 'conn-steam-live',
      platformId: 'STEAM_CN',
      processId: 33548,
      playerId: 1556514,
      status: 'CONNECTED',
    },
    {
      connectionId: 'conn-taptap-live',
      platformId: 'TAPTAP',
      processId: 28624,
      playerId: null,
      status: 'DISCONNECTED',
    },
  ];

  const result = resolver.resolve(sessions, 'USER_SPECTATOR');

  // Main session is intact
  assert.strictEqual(result.mainSession?.role, 'MAIN');
  assert.strictEqual(result.mainSession?.playerId, 1556514);
  assert.strictEqual(result.mainSession?.status, 'CONNECTED');

  // Spectator session is disconnected
  assert.strictEqual(result.spectatorSession, null);
  assert.strictEqual(result.selectedSpectatorSession, null);
  assert.strictEqual(result.isSpectatorReady, false);

  // Spectator prompt points to TapTap without demanding Steam logout
  assert.strictEqual(
    result.spectatorPrompt?.includes('3045259') ?? false,
    true,
    'Spectator prompt mentions bound TapTap account 3045259'
  );
  assert.strictEqual(
    result.spectatorPrompt?.includes('Steam') ?? false,
    false,
    'Spectator prompt MUST NEVER instruct user to log out of Steam'
  );
});

runTest('Suite 3.3: Steam MAIN disconnects -> TapTap spectator streaming remains completely unaffected', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);

  gameAccountSvc.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', '我的主账号');
  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '专用观战账号');

  const resolver = new RuntimeSessionResolver(gameAccountSvc.getBindings());

  // Steam closes, TapTap remains in active match
  const sessions: RuntimeSession[] = [
    {
      connectionId: 'conn-steam-live',
      platformId: 'STEAM_CN',
      processId: 33548,
      playerId: null,
      status: 'DISCONNECTED',
    },
    {
      connectionId: 'conn-taptap-live',
      platformId: 'TAPTAP',
      processId: 28624,
      playerId: 3045259,
      status: 'CONNECTED',
    },
  ];

  const result = resolver.resolve(sessions, 'USER_SPECTATOR');

  assert.strictEqual(result.mainSession, null, 'Main session is disconnected');
  assert.strictEqual(result.spectatorSession?.role, 'USER_SPECTATOR');
  assert.strictEqual(result.spectatorSession?.playerId, 3045259);
  assert.strictEqual(result.selectedSpectatorSession?.connectionId, 'conn-taptap-live');
  assert.strictEqual(result.isSpectatorReady, true);
});

// =============================================================================
// SUITE 4: REPAIRED ACCOUNT BINDING TRUTH & ZERO AUTO-BINDING
// =============================================================================
console.log('\n--- SUITE 4: Repaired Account Binding Truth & Non-Auto-Binding Guarantee ---');

runTest('Suite 4.1: Explicit spectator channel is preserved across reload', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);

  // The user can choose Steam as the spectator channel. Do not silently
  // replace it with the developer's historical TapTap configuration.
  const binding = gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'STEAM_CN', '我的专用观战账号');
  assert.strictEqual(binding.playerId, 3045259);
  assert.strictEqual(binding.role, GameAccountRole.USER_SPECTATOR);
  assert.strictEqual(binding.platformId, 'STEAM_CN', 'Explicit spectator channel is retained');

  // Verify reload preserves the explicit channel.
  const reloaded = gameAccountSvc.reloadBindings();
  const specBinding = reloaded.find((b) => b.role === GameAccountRole.USER_SPECTATOR);
  assert.strictEqual(specBinding?.playerId, 3045259);
  assert.strictEqual(specBinding?.platformId, 'STEAM_CN');
});

runTest('Suite 4.2: Loading stored bindings never substitutes historical developer accounts', () => {
  const storage = new MemoryStorage();

  // Stored bindings describe user intent, not proof of a live login.
  const contaminatedRecord = [
    {
      bindingId: 'bind-legacy-contaminated',
      platformId: 'STEAM_CN',
      playerId: 212667,
      role: 'USER_SPECTATOR',
      boundAt: '2026-10-18T10:00:00Z',
      displayName: '观战账号',
      lastSeenAt: null,
    },
  ];
  storage.setItem(STORAGE_KEY_GAME_ACCOUNT_BINDINGS, JSON.stringify(contaminatedRecord));

  // Reload keeps the account choice; an absent runtime still has no live ID.
  const service = new GameAccountService(storage);
  const bindings = service.getBindings();
  assert.strictEqual(bindings.length, 1);

  const repaired = bindings[0];
  assert.strictEqual(repaired.playerId, 212667, 'Loading does not invent a replacement account');
  assert.strictEqual(repaired.platformId, 'STEAM_CN', 'Loading does not invent a replacement channel');
  assert.strictEqual(repaired.role, GameAccountRole.USER_SPECTATOR);
  assert.strictEqual(service.resolveSession(null, 'STEAM_CN', false).currentSessionPlayerId, null);
});

runTest('Suite 4.3: Unbound accounts resolve to UNBOUND_ACCOUNT with zero automatic binding', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);

  gameAccountSvc.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', '主账号');
  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '观战账号');

  const initialBindingCount = gameAccountSvc.getBindings().length;
  assert.strictEqual(initialBindingCount, 2);

  // Unknown player ID arrives
  const unknownPid = 7777777;
  const resolved = gameAccountSvc.resolveSession(unknownPid, 'STEAM_CN', true);

  assert.strictEqual(resolved.resolvedRole, ResolvedAccountRole.UNBOUND_ACCOUNT);
  assert.strictEqual(resolved.currentSessionPlayerId, unknownPid);
  assert.strictEqual(resolved.binding, null, 'Binding must be strictly null for unbound account');

  // Verify storage was NOT modified (NO AUTO-BINDING!)
  const bindingsAfter = gameAccountSvc.getBindings();
  assert.strictEqual(bindingsAfter.length, 2, 'Bindings count remains unchanged');
  assert.strictEqual(bindingsAfter.some((b) => b.playerId === unknownPid), false, 'Unknown account was not auto-bound');
});

// =============================================================================
// SUITE 5: ZERO CREDENTIAL STORAGE AND SECURITY AUDIT
// =============================================================================
console.log('\n--- SUITE 5: Zero-Credential Storage and Whitelist Audit ---');

runTest('Suite 5.1: Prohibits storing passwords, tokens, auth keys or secrets', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);

  for (const forbidden of FORBIDDEN_SECURITY_KEYS) {
    assert.throws(
      () => {
        gameAccountSvc.bindAccount(1234567, GameAccountRole.MAIN, 'STEAM_CN', 'Player', {
          [forbidden]: 'malicious_secret_value',
        });
      },
      /SECURITY_VIOLATION/,
      `Must throw SECURITY_VIOLATION on forbidden key: "${forbidden}"`
    );
  }
});

runTest('Suite 5.2: Storage JSON strictly contains only whitelisted metadata', () => {
  const storage = new MemoryStorage();
  const gameAccountSvc = new GameAccountService(storage);

  gameAccountSvc.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', '主账号');
  gameAccountSvc.bindAccount(3045259, GameAccountRole.USER_SPECTATOR, 'TAPTAP', '专用观战账号');

  const rawJson = storage.getItem(STORAGE_KEY_GAME_ACCOUNT_BINDINGS)!;
  assert.ok(rawJson, 'Storage item exists');

  for (const forbidden of FORBIDDEN_SECURITY_KEYS) {
    assert.strictEqual(rawJson.toLowerCase().includes(`"${forbidden}"`), false, `Raw JSON must not contain "${forbidden}"`);
  }

  const parsed = JSON.parse(rawJson);
  assert.strictEqual(Array.isArray(parsed), true);
  const allowedKeys = new Set(['bindingId', 'platformId', 'playerId', 'displayName', 'role', 'boundAt', 'lastSeenAt']);

  for (const item of parsed) {
    for (const key of Object.keys(item)) {
      assert.strictEqual(allowedKeys.has(key), true, `Key "${key}" must be in allowed whitelist`);
    }
  }
});

// =============================================================================
// SUMMARY REPORT
// =============================================================================
console.log('\n' + '='.repeat(80));
console.log(`IDENTITY ISOLATION REGRESSION RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
console.log('='.repeat(80));

if (failCount > 0) {
  process.exit(1);
}
