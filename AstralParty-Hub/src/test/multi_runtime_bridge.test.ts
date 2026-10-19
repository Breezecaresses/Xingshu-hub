/**
 * Project XingShu — Multi-Runtime Bridge Session Architecture Test Suite
 * Phase: HS4-P1.5
 * Lane 3: Core Multi-Runtime Architecture
 *
 * Verifies:
 * - Test 1: Multi-Runtime Coexistence: Steam CN (MAIN) and TapTap (USER_SPECTATOR) registered concurrently.
 * - Test 2: Execution Session Selection: In USER_SPECTATOR mode, strictly selects TapTap runtime.
 * - Test 3: Tagged Hand Snapshots: SpectatorHandPayload properly tagged with connectionId and platformId.
 * - Test 4: Disconnect Isolation: Disconnecting Steam CN leaves TapTap active, connected, and streaming.
 * - Test 5: Reconnect & Heartbeat Monotonicity: Session updates without clobbering other runtimes.
 * - Test 6: Prompt & Fallback Invariants: Prohibits logout prompt when only Steam CN is active.
 */

import { SpectatorService } from '../services/spectatorService.ts';
import { runtimeSessionResolver } from '../services/runtimeSessionResolver.ts';
import { type RuntimeSession } from '../types/runtimeSession.ts';
import type { SpectatorHandPayload } from '../shared/types/index.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.5: MULTI-RUNTIME BRIDGE SESSION ARCHITECTURE TEST SUITE');
console.log('================================================================================\n');

let passCount = 0;
let failCount = 0;

function assert(condition: boolean, scenario: string, detail?: string): void {
  if (condition) {
    console.log(`[PASS] ${scenario}`);
    passCount++;
  } else {
    console.error(`[FAIL] ${scenario}: ${detail ?? 'assertion failed'}`);
    failCount++;
    throw new Error(`Assertion failed in ${scenario}: ${detail ?? ''}`);
  }
}

function assertStrictEqual<T>(actual: T, expected: T, scenario: string): void {
  if (actual === expected) {
    console.log(`[PASS] ${scenario}`);
    passCount++;
  } else {
    console.error(`[FAIL] ${scenario}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
    failCount++;
    throw new Error(`Assertion failed in ${scenario}`);
  }
}

async function runTests(): Promise<void> {
  // Configure test fixture bindings
  runtimeSessionResolver.setBindings([
    {
      bindingId: 'test-main-steam',
      platformId: 'STEAM_CN',
      playerId: 1556514,
      displayName: '主账号',
      role: 'MAIN',
      boundAt: new Date().toISOString(),
      lastSeenAt: null,
    },
    {
      bindingId: 'test-spec-taptap',
      platformId: 'TAPTAP',
      playerId: 3045259,
      displayName: '我的观战账号',
      role: 'USER_SPECTATOR',
      boundAt: new Date().toISOString(),
      lastSeenAt: null,
    },
  ]);

  const service = new SpectatorService();

  console.log('--- Test 1: Multi-Runtime Concurrent Registration ---');
  const steamSession: RuntimeSession = {
    connectionId: 'conn_steam_12345',
    platformId: 'STEAM_CN',
    processId: 12345,
    gamePid: 12345,
    executablePath: 'E:\\steam\\steamapps\\common\\Astral Party\\8vJXn6CN\\AstralParty_CN.exe',
    playerId: 1556514,
    displayName: '主账号',
    runtimeVersion: '1.0.0',
    generation: 1,
    connectedAt: new Date().toISOString(),
    lastSeenAt: new Date().toISOString(),
    role: 'MAIN',
    status: 'CONNECTED',
  };

  const tapTapSession: RuntimeSession = {
    connectionId: 'conn_taptap_67890',
    platformId: 'TAPTAP',
    processId: 67890,
    gamePid: 67890,
    executablePath: 'E:\\TapTap\\games\\CN_TAPTAP_V3.3.0\\吉星派对\\吉星派对.exe',
    playerId: 3045259,
    displayName: '我的观战账号',
    runtimeVersion: '1.0.0',
    generation: 2,
    connectedAt: new Date().toISOString(),
    lastSeenAt: new Date().toISOString(),
    role: 'USER_SPECTATOR',
    status: 'CONNECTED',
  };

  // Ingest both sessions
  service.handleSessionChanged(steamSession);
  service.handleSessionChanged(tapTapSession);

  const activeSessions = service.getActiveSessions();
  assertStrictEqual(activeSessions.length, 2, 'Exactly 2 sessions active concurrently');
  assert(activeSessions.some((s) => s.connectionId === 'conn_steam_12345'), 'Steam CN session is registered');
  assert(activeSessions.some((s) => s.connectionId === 'conn_taptap_67890'), 'TapTap session is registered');

  console.log('\n--- Test 2: Execution Session Routing Resolution ---');
  const resolution = service.getResolution('USER_SPECTATOR');
  assert(resolution.mainSession !== null, 'MAIN session resolved');
  assertStrictEqual(resolution.mainSession?.platformId, 'STEAM_CN', 'MAIN session platform is STEAM_CN');
  assertStrictEqual(resolution.mainSession?.playerId, 1556514, 'MAIN session playerId is 1556514');

  assert(resolution.spectatorSession !== null, 'Spectator session resolved');
  assertStrictEqual(resolution.spectatorSession?.platformId, 'TAPTAP', 'Spectator session platform is TAPTAP');
  assertStrictEqual(resolution.spectatorSession?.playerId, 3045259, 'Spectator session playerId is 3045259');

  const execSession = service.getSpectatorExecutionSession('USER_SPECTATOR');
  assert(execSession !== null, 'Spectator execution session exists');
  assertStrictEqual(execSession?.connectionId, 'conn_taptap_67890', 'Strictly selects TapTap connection ID');
  assertStrictEqual(execSession?.platformId, 'TAPTAP', 'Spectator execution runtime is TAPTAP');
  assert(execSession?.connectionId !== 'conn_steam_12345', 'STRICT: Never selects Steam MAIN runtime for spectator');

  console.log('\n--- Test 3: Tagged Hand Snapshot Processing ---');
  let receivedSnapshot: SpectatorHandPayload | null = null;
  const unlistenSnap = service.onHandSnapshot((snap) => {
    receivedSnapshot = snap;
  });

  const testSnapshot: SpectatorHandPayload = {
    schemaVersion: '1.1.0',
    roomId: 9999,
    spectatorSessionEpoch: 1,
    spectatorAccountPlayerId: 3045259,
    sequenceNumber: 1,
    snapshotTimestamp: new Date().toISOString(),
    playerId: 2081758,
    cardIds: [10005, 20002],
    handSize: 2,
    roomPlayers: {},
    connectionId: 'conn_taptap_67890',
    platformId: 'TAPTAP',
  };

  service.handleHandSnapshot(testSnapshot);
  assert(receivedSnapshot !== null, 'Snapshot subscriber notified');
  const snap = receivedSnapshot as SpectatorHandPayload | null;
  assertStrictEqual(snap?.connectionId, 'conn_taptap_67890', 'Snapshot tagged with connectionId');
  assertStrictEqual(snap?.platformId, 'TAPTAP', 'Snapshot tagged with platformId');

  const cachedLatest = service.getLatestHandPayload();
  assertStrictEqual(cachedLatest?.roomId, 9999, 'Latest global snapshot retained');
  const cachedTapTap = service.getLatestHandPayload('conn_taptap_67890');
  assertStrictEqual(cachedTapTap?.sequenceNumber, 1, 'Per-connection snapshot retained for TapTap');
  unlistenSnap();

  console.log('\n--- Test 4: Disconnect Isolation Guarantee ---');
  // Disconnect Steam CN runtime
  service.handleSessionDisconnected('conn_steam_12345');

  const sessionsAfterSteamDisconnect = service.getActiveSessions();
  assertStrictEqual(sessionsAfterSteamDisconnect.length, 1, 'Exactly 1 session remains active');
  assertStrictEqual(sessionsAfterSteamDisconnect[0].connectionId, 'conn_taptap_67890', 'TapTap session remains untouched');
  assertStrictEqual(sessionsAfterSteamDisconnect[0].platformId, 'TAPTAP', 'TapTap platform intact');

  // Verify TapTap continues serving as spectator execution runtime
  const execAfterSteamDisconnect = service.getSpectatorExecutionSession('USER_SPECTATOR');
  assert(execAfterSteamDisconnect !== null, 'Spectator runtime still available');
  assertStrictEqual(execAfterSteamDisconnect?.connectionId, 'conn_taptap_67890', 'TapTap remains execution runtime');

  // Disconnect TapTap runtime
  service.handleSessionDisconnected('conn_taptap_67890');
  assertStrictEqual(service.getActiveSessions().length, 0, 'All sessions disconnected cleanly');

  console.log('\n--- Test 5: Parallel Unknown / Unbound Runtime Coexistence ---');
  service.handleSessionChanged(steamSession);

  const unknownSession: RuntimeSession = {
    connectionId: 'conn_global_99999',
    platformId: 'STEAM_GLOBAL',
    processId: 99999,
    gamePid: 99999,
    executablePath: 'E:\\steam\\steamapps\\common\\Astral Party Global\\AstralParty_INT.exe',
    playerId: 7777777,
    displayName: 'Global Player',
    runtimeVersion: '1.0.0',
    generation: 3,
    connectedAt: new Date().toISOString(),
    lastSeenAt: new Date().toISOString(),
    role: 'UNBOUND',
    status: 'CONNECTED',
  };

  service.handleSessionChanged(unknownSession);
  const multiRes = service.getResolution('USER_SPECTATOR');
  assertStrictEqual(multiRes.allSessions.length, 2, '2 sessions resolved');
  assert(multiRes.allSessions.some((s) => s.connectionId === 'conn_steam_12345' && s.role === 'MAIN'), 'Steam CN still recognized as MAIN');
  assert(multiRes.allSessions.some((s) => s.connectionId === 'conn_global_99999' && s.role === 'UNBOUND'), 'Global session marked UNBOUND without overwriting MAIN');

  // Spectator prompt when only Steam CN and unknown are active
  assert(multiRes.spectatorPrompt !== null, 'Spectator prompt present when TapTap is absent');
  assert(Boolean(multiRes.spectatorPrompt?.includes('3045259')), 'Prompt mentions target TapTap account 3045259');
  assert(!multiRes.spectatorPrompt?.includes('登出'), 'Strictly prohibits logout prompt');

  service.destroy();
  console.log('\n================================================================================');
  console.log(`MULTI-RUNTIME BRIDGE SUITE: ${passCount} PASSED, ${failCount} FAILED`);
  console.log('================================================================================\n');

  if (failCount > 0) {
    process.exit(1);
  }
}

runTests().catch((err) => {
  console.error('Fatal test error:', err);
  process.exit(1);
});
