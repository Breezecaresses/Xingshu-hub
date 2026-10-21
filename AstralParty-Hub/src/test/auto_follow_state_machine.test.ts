/**
 * Project XingShu — HS4-P1.6: Auto-Follow Spectator State Machine & Regression Test Suite
 *
 * Comprehensive automated regression testing covering:
 * 1. Initial State & Invariants
 * 2. Match Detection & Spectator Ready Transitions
 * 3. Happy Path Complete Lifecycle (IDLE -> MAIN_IN_MATCH -> SPECTATOR_READY -> AUTO_JOINING -> SPECTATING -> MAIN_MATCH_ENDED -> IDLE)
 * 4. Offline Spectator Bootstrapping (OFFLINE -> LAUNCHING -> WAITING_RUNTIME -> WAITING_LOGIN -> READY -> JOINING)
 * 5. Error Recovery: Launch Timeout
 * 6. Error Recovery: Runtime Connection Timeout
 * 7. Error Recovery: Wrong Account Logged In
 * 8. Error Recovery: Join Rejected / Timeout / Invalid Target
 * 9. Race Condition: Match End During Join
 * 10. Disconnect Handling (during join & during spectating)
 * 11. Fallback Manual Spectator Code Entry
 * 12. Strict Security Routing Verification (TAPTAP only, NEVER Steam MAIN)
 */

import {
  AutoFollowStateMachine,
  processAutoFollowTransition,
  createInitialAutoFollowContext,
  verifyWatchTargetSecurity,
  CANONICAL_BOUND_MAIN_PLAYER_ID,
  CANONICAL_BOUND_SPECTATOR_PLAYER_ID,
  CANONICAL_SPECTATOR_PLATFORM,
  CANONICAL_MAIN_PLATFORM,
  type AutoFollowContext,
  type WatchTargetResponse,
} from '../services/autoFollowStateMachine.ts';

declare const process: { exit(code?: number): never };

console.log('================================================================================');
console.log('PROJECT XINGSHU — HS4-P1.6: AUTO-FOLLOW STATE MACHINE & REGRESSION TEST SUITE');
console.log('================================================================================');

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

// =============================================================================
// Test Group 1: Initial State & Invariants
// =============================================================================
console.log('\n--- Test Group 1: Initial State & Invariants ---');

runTest('Initial state defaults to IDLE with correct canonical bindings', () => {
  const sm = new AutoFollowStateMachine();
  const ctx = sm.getContext();

  assertStrictEqual(sm.getState(), 'IDLE');
  assertStrictEqual(ctx.mainPlayerId, 1556514);
  assertStrictEqual(ctx.expectedSpectatorPlayerId, 3045259);
  assertStrictEqual(ctx.targetPlatform, 'TAPTAP');
  assertStrictEqual(ctx.activeTarget, null);
  assertStrictEqual(ctx.retryCount, 0);
  assertStrictEqual(ctx.failureReason, null);
});

// =============================================================================
// Test Group 2: Match Detection & Spectator Ready Transitions
// =============================================================================
console.log('\n--- Test Group 2: Match Detection & Spectator Ready Transitions ---');

runTest('Match detection transitions to SPECTATOR_READY when TapTap runtime already verified', () => {
  const sm = new AutoFollowStateMachine({
    spectatorPid: 38376,
    spectatorConnectionId: 'conn_tap_38376',
    spectatorPlayerId: 3045259,
  });

  const res = sm.dispatch({
    type: 'MAIN_MATCH_DETECTED',
    target: { roomId: 7777, watchCode: 'ABCDEF' },
  });

  assertStrictEqual(res.nextState, 'SPECTATOR_READY');
  assertStrictEqual(res.actionRequired, 'SEND_WATCH_TARGET');
  assert(sm.getContext().activeTarget?.roomId === 7777, 'activeTarget must hold roomId 7777');
});

runTest('SPECTATOR_READY transitions to AUTO_JOINING on WATCH_TARGET_DISPATCHED', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'SPECTATOR_READY',
    spectatorConnectionId: 'conn_tap_38376',
    spectatorPlayerId: 3045259,
    activeTarget: { roomId: 7777, watchCode: 'ABCDEF' },
  });

  const res = sm.dispatch({
    type: 'WATCH_TARGET_DISPATCHED',
    requestId: 'req-uuid-1234',
    connectionId: 'conn_tap_38376',
  });

  assertStrictEqual(res.nextState, 'AUTO_JOINING');
  assertStrictEqual(sm.getContext().lastRequestId, 'req-uuid-1234');
});

// =============================================================================
// Test Group 3: Happy Path Complete Lifecycle
// =============================================================================
console.log('\n--- Test Group 3: Happy Path Complete Lifecycle ---');

runTest('Complete happy path: IDLE -> READY -> AUTO_JOINING -> SPECTATING -> ENDED -> IDLE', () => {
  const sm = new AutoFollowStateMachine({
    spectatorPid: 38376,
    spectatorConnectionId: 'conn_tap_38376',
    spectatorPlayerId: 3045259,
  });

  // 1. Match detected
  let r = sm.dispatch({
    type: 'MAIN_MATCH_DETECTED',
    target: { roomId: 7777, watchCode: 'ABCDEF' },
  });
  assertStrictEqual(r.nextState, 'SPECTATOR_READY');

  // 2. Watch target dispatched
  r = sm.dispatch({
    type: 'WATCH_TARGET_DISPATCHED',
    requestId: 'req-1',
    connectionId: 'conn_tap_38376',
  });
  assertStrictEqual(r.nextState, 'AUTO_JOINING');

  // 3. Runtime acknowledges acceptance
  r = sm.dispatch({
    type: 'WATCH_RESPONSE_RECEIVED',
    response: {
      status: 'WATCH_JOIN_ACCEPTED',
      requestId: 'req-1',
      reason: 'ACCEPTED',
    },
  });
  assertStrictEqual(r.nextState, 'AUTO_JOINING', 'Accepted remains joining until snapshot');

  // 4. First snapshot arrives for room 7777 -> SPECTATING
  r = sm.dispatch({
    type: 'HAND_SNAPSHOT_RECEIVED',
    roomId: 7777,
    epoch: 1,
    sequence: 1,
  });
  assertStrictEqual(r.nextState, 'SPECTATING');

  // 5. Subsequent live snapshots keep SPECTATING
  r = sm.dispatch({
    type: 'HAND_SNAPSHOT_RECEIVED',
    roomId: 7777,
    epoch: 1,
    sequence: 2,
  });
  assertStrictEqual(r.nextState, 'SPECTATING');

  // 6. Main player match ends
  r = sm.dispatch({
    type: 'MAIN_MATCH_ENDED',
    reason: 'MATCH_COMPLETED',
  });
  assertStrictEqual(r.nextState, 'MAIN_MATCH_ENDED');

  // 7. Reset to IDLE
  r = sm.dispatch({ type: 'RESET_TO_IDLE' });
  assertStrictEqual(r.nextState, 'IDLE');
  assertStrictEqual(sm.getContext().activeTarget, null);
});

// =============================================================================
// Test Group 4: Offline Spectator Bootstrapping
// =============================================================================
console.log('\n--- Test Group 4: Offline Spectator Bootstrapping ---');

runTest('Full bootstrap sequence when spectator client starts completely offline', () => {
  const sm = new AutoFollowStateMachine(); // All null / offline

  // 1. Match detected -> client offline
  let r = sm.dispatch({
    type: 'MAIN_MATCH_DETECTED',
    target: { roomId: 8888, watchCode: 'SPECT1' },
  });
  assertStrictEqual(r.nextState, 'SPECTATOR_CLIENT_OFFLINE');
  assertStrictEqual(r.actionRequired, 'LAUNCH_CLIENT');

  // 2. Hub triggers client launch
  r = sm.dispatch({ type: 'CLIENT_LAUNCH_INITIATED' });
  assertStrictEqual(r.nextState, 'LAUNCHING_SPECTATOR_CLIENT');

  // 3. Process appears
  r = sm.dispatch({ type: 'PROCESS_STATUS', isRunning: true, pid: 45678 });
  assertStrictEqual(r.nextState, 'WAITING_SPECTATOR_RUNTIME');
  assertStrictEqual(sm.getContext().spectatorPid, 45678);

  // 4. XingShu runtime connects via loopback TCP without playerId (pending)
  r = sm.dispatch({
    type: 'RUNTIME_CONNECTED',
    connectionId: 'conn_tap_45678',
    platformId: 'TAPTAP',
    playerId: null,
  });
  assertStrictEqual(r.nextState, 'WAITING_SPECTATOR_LOGIN');

  // 5. Player identity arrives matching 3045259
  r = sm.dispatch({ type: 'IDENTITY_RESOLVED', playerId: 3045259 });
  assertStrictEqual(r.nextState, 'SPECTATOR_READY');
  assertStrictEqual(r.actionRequired, 'SEND_WATCH_TARGET');

  // 6. Watch dispatched
  r = sm.dispatch({
    type: 'WATCH_TARGET_DISPATCHED',
    requestId: 'req-bootstrap',
    connectionId: 'conn_tap_45678',
  });
  assertStrictEqual(r.nextState, 'AUTO_JOINING');
});

// =============================================================================
// Test Group 5: Error Recovery - Launch Timeout
// =============================================================================
console.log('\n--- Test Group 5: Error Recovery - Launch Timeout ---');

runTest('Client launch timeout transitions to AUTO_JOIN_FAILED and retry recovers', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'LAUNCHING_SPECTATOR_CLIENT',
    activeTarget: { roomId: 7777 },
  });

  let r = sm.dispatch({ type: 'CLIENT_LAUNCH_TIMEOUT' });
  assertStrictEqual(r.nextState, 'AUTO_JOIN_FAILED');
  assertStrictEqual(sm.getContext().failureReason, 'CLIENT_LAUNCH_FAILED');

  // User clicks retry
  r = sm.dispatch({ type: 'RETRY_AUTO_JOIN' });
  assertStrictEqual(r.nextState, 'LAUNCHING_SPECTATOR_CLIENT');
  assertStrictEqual(sm.getContext().retryCount, 1);
  assertStrictEqual(sm.getContext().failureReason, null);
});

// =============================================================================
// Test Group 6: Error Recovery - Runtime Connection Timeout
// =============================================================================
console.log('\n--- Test Group 6: Error Recovery - Runtime Connection Timeout ---');

runTest('Runtime connection timeout transitions to AUTO_JOIN_FAILED and retry recovers', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'WAITING_SPECTATOR_RUNTIME',
    spectatorPid: 38376,
    activeTarget: { roomId: 7777 },
  });

  let r = sm.dispatch({ type: 'RUNTIME_TIMEOUT' });
  assertStrictEqual(r.nextState, 'AUTO_JOIN_FAILED');
  assertStrictEqual(sm.getContext().failureReason, 'RUNTIME_CONNECT_TIMEOUT');

  // User clicks retry -> process still alive -> returns to WAITING_SPECTATOR_RUNTIME
  r = sm.dispatch({ type: 'RETRY_AUTO_JOIN' });
  assertStrictEqual(r.nextState, 'WAITING_SPECTATOR_RUNTIME');
});

// =============================================================================
// Test Group 7: Error Recovery - Wrong Account Logged In
// =============================================================================
console.log('\n--- Test Group 7: Error Recovery - Wrong Account Logged In ---');

runTest('Wrong account (9999999 instead of 3045259) triggers AUTO_JOIN_FAILED', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'WAITING_SPECTATOR_LOGIN',
    spectatorConnectionId: 'conn_tap_1',
    activeTarget: { roomId: 7777 },
  });

  const r = sm.dispatch({ type: 'IDENTITY_RESOLVED', playerId: 9999999 });
  assertStrictEqual(r.nextState, 'AUTO_JOIN_FAILED');
  assertStrictEqual(sm.getContext().failureReason, 'WRONG_ACCOUNT_LOGGED_IN');
  assert(sm.getContext().failureMessage?.includes('9999999'), 'Failure message must mention incorrect account');
});

// =============================================================================
// Test Group 8: Error Recovery - Join Rejection, Timeout, and Invalid Target
// =============================================================================
console.log('\n--- Test Group 8: Error Recovery - Join Rejection, Timeout, and Invalid Target ---');

runTest('WATCH_JOIN_REJECTED transitions to AUTO_JOIN_FAILED', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'AUTO_JOINING',
    activeTarget: { roomId: 7777 },
  });

  const r = sm.dispatch({
    type: 'WATCH_RESPONSE_RECEIVED',
    response: {
      status: 'WATCH_JOIN_REJECTED',
      requestId: 'req-rej',
      reason: 'ROOM_AUDIENCE_LIMIT_REACHED',
    },
  });

  assertStrictEqual(r.nextState, 'AUTO_JOIN_FAILED');
  assertStrictEqual(sm.getContext().failureReason, 'WATCH_JOIN_REJECTED');
});

runTest('WATCH_JOIN_TIMEOUT transitions to AUTO_JOIN_FAILED', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'AUTO_JOINING',
    activeTarget: { roomId: 7777 },
  });

  const r = sm.dispatch({
    type: 'WATCH_RESPONSE_RECEIVED',
    response: {
      status: 'WATCH_JOIN_TIMEOUT',
      requestId: 'req-to',
    },
  });

  assertStrictEqual(r.nextState, 'AUTO_JOIN_FAILED');
  assertStrictEqual(sm.getContext().failureReason, 'WATCH_JOIN_TIMEOUT');
});

runTest('WATCH_TARGET_INVALID transitions to AUTO_JOIN_FAILED', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'AUTO_JOINING',
    activeTarget: { roomId: 7777 },
  });

  const r = sm.dispatch({
    type: 'WATCH_RESPONSE_RECEIVED',
    response: {
      status: 'WATCH_TARGET_INVALID',
      requestId: 'req-inv',
      reason: 'ROOM_NOT_FOUND',
    },
  });

  assertStrictEqual(r.nextState, 'AUTO_JOIN_FAILED');
  assertStrictEqual(sm.getContext().failureReason, 'WATCH_TARGET_INVALID');
});

runTest('WATCH_ALREADY_JOINED immediately transitions to SPECTATING', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'AUTO_JOINING',
    activeTarget: { roomId: 7777 },
  });

  const r = sm.dispatch({
    type: 'WATCH_RESPONSE_RECEIVED',
    response: {
      status: 'WATCH_ALREADY_JOINED',
      requestId: 'req-already',
    },
  });

  assertStrictEqual(r.nextState, 'SPECTATING');
});

// =============================================================================
// Test Group 9: Race Condition - Match End During Join
// =============================================================================
console.log('\n--- Test Group 9: Race Condition - Match End During Join ---');

runTest('Main match ends while in AUTO_JOINING cleanly aborts to MAIN_MATCH_ENDED', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'AUTO_JOINING',
    activeTarget: { roomId: 7777 },
  });

  const r = sm.dispatch({ type: 'MAIN_MATCH_ENDED', reason: 'ABORT' });
  assertStrictEqual(r.nextState, 'MAIN_MATCH_ENDED');
});

// =============================================================================
// Test Group 10: Disconnect Recovery
// =============================================================================
console.log('\n--- Test Group 10: Disconnect Recovery ---');

runTest('Spectator disconnect during AUTO_JOINING transitions to AUTO_JOIN_FAILED', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'AUTO_JOINING',
    spectatorConnectionId: 'conn_active',
    activeTarget: { roomId: 7777 },
  });

  const r = sm.dispatch({ type: 'SPECTATOR_DISCONNECTED', connectionId: 'conn_active' });
  assertStrictEqual(r.nextState, 'AUTO_JOIN_FAILED');
  assertStrictEqual(sm.getContext().failureReason, 'DISCONNECTED_DURING_JOIN');
  assertStrictEqual(sm.getContext().spectatorConnectionId, null);
});

runTest('Spectator disconnect while SPECTATING transitions to AUTO_JOIN_FAILED', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'SPECTATING',
    spectatorConnectionId: 'conn_active',
    activeTarget: { roomId: 7777 },
  });

  const r = sm.dispatch({ type: 'SPECTATOR_DISCONNECTED', connectionId: 'conn_active' });
  assertStrictEqual(r.nextState, 'AUTO_JOIN_FAILED');
  assertStrictEqual(sm.getContext().failureReason, 'SPECTATOR_DISCONNECTED');
});

// =============================================================================
// Test Group 11: Fallback Manual Spectator Code Entry
// =============================================================================
console.log('\n--- Test Group 11: Fallback Manual Spectator Code Entry ---');

runTest('Manual code entry while in AUTO_JOIN_FAILED triggers AUTO_JOINING if runtime ready', () => {
  const sm = new AutoFollowStateMachine({
    currentState: 'AUTO_JOIN_FAILED',
    spectatorPid: 38376,
    spectatorConnectionId: 'conn_tap_ready',
    spectatorPlayerId: 3045259,
    failureReason: 'WATCH_JOIN_REJECTED',
  });

  const r = sm.dispatch({
    type: 'MANUAL_FALLBACK_CODE_ENTERED',
    watchCode: 'XYZ999',
    roomId: 9999,
  });

  assertStrictEqual(r.nextState, 'AUTO_JOINING');
  assertStrictEqual(r.actionRequired, 'SEND_WATCH_TARGET');
  assertStrictEqual(sm.getContext().manualFallbackCode, 'XYZ999');
  assertStrictEqual(sm.getContext().activeTarget?.roomId, 9999);
  assertStrictEqual(sm.getContext().failureReason, null);
});

// =============================================================================
// Test Group 12: Strict Security Guard Verification
// =============================================================================
console.log('\n--- Test Group 12: Strict Security Guard Verification ---');

runTest('Security Guard strictly REJECTS routing WATCH_TARGET to Steam MAIN', () => {
  const steamMainSession = {
    connectionId: 'conn_steam_33548',
    platformId: 'STEAM_CN',
    playerId: 1556514,
  };

  const check = verifyWatchTargetSecurity(steamMainSession, 3045259);
  assertStrictEqual(check.allowed, false);
  assert(
    check.violationReason?.includes("bound spectator platform 'TAPTAP'"),
    'Security error must identify the expected bound spectator platform'
  );
});

runTest('Security Guard strictly REJECTS routing to TapTap with wrong player ID', () => {
  const wrongTapSession = {
    connectionId: 'conn_tap_wrong',
    platformId: 'TAPTAP',
    playerId: 9999999,
  };

  const check = verifyWatchTargetSecurity(wrongTapSession, 3045259);
  assertStrictEqual(check.allowed, false);
  assert(
    check.violationReason?.includes('does not match bound USER_SPECTATOR'),
    'Security error must flag mismatched player ID'
  );
});

runTest('Security Guard ALLOWS routing to verified TapTap spectator session (3045259)', () => {
  const validTapSession = {
    connectionId: 'conn_tap_38376',
    platformId: 'TAPTAP',
    playerId: 3045259,
  };

  const check = verifyWatchTargetSecurity(validTapSession, 3045259);
  assertStrictEqual(check.allowed, true);
  assertStrictEqual(check.violationReason, undefined);
});

runTest('Security Guard REJECTS routing to TapTap when playerId is pending (null)', () => {
  const pendingTapSession = {
    connectionId: 'conn_tap_38376',
    platformId: 'TAPTAP',
    playerId: null,
  };

  const check = verifyWatchTargetSecurity(pendingTapSession, 3045259);
  assertStrictEqual(check.allowed, false);
});

// =============================================================================
// SUMMARY
// =============================================================================
runTest('Configured Bilibili and Steam spectator bindings route without controlling MAIN', () => {
  for (const platform of ['BILIBILI', 'STEAM_CN', 'STEAM_GLOBAL', 'WEGAME']) {
    const sm = new AutoFollowStateMachine();
    sm.configureBindings(111, 222, platform);
    sm.dispatch({ type: 'RUNTIME_CONNECTED', connectionId: 'spectator', platformId: platform, playerId: 222 });
    const join = sm.dispatch({ type: 'MAIN_MATCH_DETECTED', target: { roomId: 71, watchCode: 'ROOM71' } });
    assertStrictEqual(join.actionRequired, 'SEND_WATCH_TARGET');
    assert(verifyWatchTargetSecurity({ connectionId: 'spectator', platformId: platform, playerId: 222 }, 222, platform).allowed, 'configured platform must pass');
    assert(!verifyWatchTargetSecurity({ connectionId: 'main', platformId: platform, playerId: 111 }, 222, platform).allowed, 'same platform main account must fail');
    sm.configureBindings(111, 333, 'TAPTAP');
    assertStrictEqual(sm.getContext().spectatorConnectionId, null, 'rebinding must invalidate previous connection');
  }
});
console.log('\n================================================================================');
console.log(`TEST RESULTS: ${passCount} PASSED, ${failCount} FAILED (TOTAL: ${passCount + failCount})`);
console.log('================================================================================');

if (failCount > 0) {
  process.exit(1);
}
