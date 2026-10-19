/**
 * Project XingShu — Runtime Session Resolver Test Suite
 * Phase: HS4-P1.5
 * Lane 4: Account-to-Runtime Routing Specialist
 *
 * Verifies:
 * - Test 1: Both Steam CN (1556514) and TapTap (3045259) connected -> resolves MAIN to Steam, USER_SPECTATOR to TapTap.
 * - Test 2: In USER_SPECTATOR mode, resolver selects TapTap connection ID as active spectator source.
 * - Test 3: TapTap connected with playerId null -> role is PENDING_SPECTATOR on TapTap platform.
 * - Test 4: TapTap disconnects -> Steam MAIN remains untouched, spectator prompt displays TapTap offline.
 * - Test 5: Only Steam connected in USER_SPECTATOR mode -> shows prompt to launch TapTap, does not prompt logout.
 * - Test 6: Unknown client connected -> marked UNBOUND without overwriting bound clients.
 * - Anti-last-connected bias verification (arrival order independence).
 * - Exact promotion verification (PENDING_SPECTATOR -> USER_SPECTATOR on packet arrival).
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';

import {
  RuntimeSessionResolver,
} from '../services/runtimeSessionResolver.ts';

import {
  type RuntimeSession,
  ResolvedRuntimeRole,
  DEFAULT_CANONICAL_BINDINGS,
} from '../types/runtimeSession.ts';

import type { GameAccountBinding } from '../types/gameAccount.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.5: RUNTIME SESSION RESOLVER TEST SUITE');
console.log('================================================================================');

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
    console.error(`[FAIL] ${scenario}: Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
    failCount++;
    throw new Error(`Assertion failed in ${scenario}: Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

function runTest(name: string, fn: () => void): void {
  try {
    fn();
  } catch (err: any) {
    console.error(`[FAIL] ${name} threw error: ${err.message}`);
  }
}

// Canonical Bindings for Tests:
// Steam CN = 1556514 (MAIN)
// TapTap   = 3045259 (USER_SPECTATOR)
const canonicalBindings: GameAccountBinding[] = [
  {
    bindingId: 'bind-main-steam',
    platformId: 'STEAM_CN',
    playerId: 1556514,
    displayName: '主游戏账号',
    role: 'MAIN',
    boundAt: '2026-10-17T00:00:00Z',
    lastSeenAt: null,
  },
  {
    bindingId: 'bind-spec-taptap',
    platformId: 'TAPTAP',
    playerId: 3045259,
    displayName: '专用观战账号',
    role: 'USER_SPECTATOR',
    boundAt: '2026-10-17T00:00:00Z',
    lastSeenAt: null,
  },
];

// -----------------------------------------------------------------------------
// Test 1: Both Steam CN (1556514) and TapTap (3045259) connected
//         -> resolves MAIN to Steam, USER_SPECTATOR to TapTap.
// -----------------------------------------------------------------------------
runTest('Test 1: Both Steam CN and TapTap connected -> resolves MAIN to Steam, USER_SPECTATOR to TapTap', () => {
  console.log('\n--- Test 1: Both Steam CN (1556514) and TapTap (3045259) connected ---');
  const resolver = new RuntimeSessionResolver(canonicalBindings);

  const steamSession: RuntimeSession = {
    connectionId: 'conn-steam-1556514',
    platformId: 'STEAM_CN',
    processId: 10100,
    playerId: 1556514,
    displayName: 'XingShuMaster',
    status: 'CONNECTED',
  };

  const tapSession: RuntimeSession = {
    connectionId: 'conn-taptap-3045259',
    platformId: 'TAPTAP',
    processId: 20200,
    playerId: 3045259,
    displayName: 'XingShuSpectatorBot',
    status: 'CONNECTED',
  };

  const result = resolver.resolve([steamSession, tapSession], 'USER_SPECTATOR');

  assertStrictEqual(result.allSessions.length, 2, 'Two sessions resolved');
  assert(result.mainSession !== null, 'MAIN session exists');
  assertStrictEqual(result.mainSession?.platformId, 'STEAM_CN', 'MAIN session platform is STEAM_CN');
  assertStrictEqual(result.mainSession?.playerId, 1556514, 'MAIN session playerId is 1556514');
  assertStrictEqual(result.mainSession?.role, 'MAIN', 'MAIN session role is strictly MAIN');

  assert(result.spectatorSession !== null, 'USER_SPECTATOR session exists');
  assertStrictEqual(result.spectatorSession?.platformId, 'TAPTAP', 'USER_SPECTATOR platform is TAPTAP');
  assertStrictEqual(result.spectatorSession?.playerId, 3045259, 'USER_SPECTATOR playerId is 3045259');
  assertStrictEqual(result.spectatorSession?.role, 'USER_SPECTATOR', 'USER_SPECTATOR session role is USER_SPECTATOR');
});

// -----------------------------------------------------------------------------
// Test 2: In USER_SPECTATOR mode, resolver selects TapTap connection ID as active spectator source
// -----------------------------------------------------------------------------
runTest('Test 2: In USER_SPECTATOR mode, resolver selects TapTap connection ID as active spectator source', () => {
  console.log('\n--- Test 2: Selects TapTap connection ID as active spectator source ---');
  const resolver = new RuntimeSessionResolver(canonicalBindings);

  const steamSession: RuntimeSession = {
    connectionId: 'conn-steam-1556514',
    platformId: 'STEAM_CN',
    processId: 10100,
    playerId: 1556514,
    status: 'CONNECTED',
  };

  const tapSession: RuntimeSession = {
    connectionId: 'conn-taptap-3045259',
    platformId: 'TAPTAP',
    processId: 20200,
    playerId: 3045259,
    status: 'CONNECTED',
  };

  // Test with Steam first, TapTap second
  const result1 = resolver.resolve([steamSession, tapSession], 'USER_SPECTATOR');
  assertStrictEqual(
    result1.selectedSpectatorConnectionId,
    'conn-taptap-3045259',
    'Selects TapTap connectionId when Steam is first'
  );
  assertStrictEqual(
    result1.selectedSpectatorSession?.platformId,
    'TAPTAP',
    'Selected spectator session is on platform TAPTAP'
  );
  assert(
    result1.selectedSpectatorConnectionId !== steamSession.connectionId,
    'STRICT: MUST NOT select Steam MAIN connection as spectator source'
  );
  assertStrictEqual(
    result1.selectedSpectatorSession?.isSpectatorExecutionRuntime,
    true,
    'TapTap session marked as isSpectatorExecutionRuntime'
  );

  // Anti-last-connected test: TapTap first, Steam connected later
  // Demonstrates resolution order does NOT depend on arrival order!
  const result2 = resolver.resolve([tapSession, steamSession], 'USER_SPECTATOR');
  assertStrictEqual(
    result2.selectedSpectatorConnectionId,
    'conn-taptap-3045259',
    'ANTI-BIAS: Selects TapTap even when Steam connected last'
  );
});

// -----------------------------------------------------------------------------
// Test 3: TapTap connected with playerId null -> role is PENDING_SPECTATOR on TapTap platform
// -----------------------------------------------------------------------------
runTest('Test 3: TapTap connected with playerId null -> role is PENDING_SPECTATOR on TapTap platform', () => {
  console.log('\n--- Test 3: TapTap connected with playerId null -> role is PENDING_SPECTATOR ---');
  const resolver = new RuntimeSessionResolver(canonicalBindings);

  const tapPendingSession: RuntimeSession = {
    connectionId: 'conn-taptap-pending-01',
    platformId: 'TAPTAP',
    processId: 20200,
    playerId: null, // Identity packet pending
    status: 'CONNECTED',
  };

  const resolved = resolver.resolveSession(tapPendingSession);

  assertStrictEqual(
    resolved.role,
    'PENDING_SPECTATOR',
    'Role is tentatively matched as PENDING_SPECTATOR via platform heuristic'
  );
  assertStrictEqual(resolved.platformId, 'TAPTAP', 'Platform is TAPTAP');
  assertStrictEqual(resolved.playerId, null, 'playerId is null while pending');
  assert(resolved.matchedBinding !== null, 'Matched tentative spectator binding');
  assertStrictEqual(resolved.matchedBinding?.playerId, 3045259, 'Tentative binding points to spectator PID 3045259');

  // Verify in full resolve() call
  const fullResult = resolver.resolve([tapPendingSession], 'USER_SPECTATOR');
  assertStrictEqual(
    fullResult.selectedSpectatorConnectionId,
    'conn-taptap-pending-01',
    'Pending TapTap session is selected for spectator execution'
  );
  assertStrictEqual(
    fullResult.selectedSpectatorSession?.role,
    'PENDING_SPECTATOR',
    'Selected session has role PENDING_SPECTATOR'
  );
});

// -----------------------------------------------------------------------------
// Test 4: TapTap disconnects -> Steam MAIN remains untouched, spectator prompt displays TapTap offline
// -----------------------------------------------------------------------------
runTest('Test 4: TapTap disconnects -> Steam MAIN remains untouched, spectator prompt displays TapTap offline', () => {
  console.log('\n--- Test 4: TapTap disconnects -> Steam MAIN remains untouched, spectator prompt displays TapTap offline ---');
  const resolver = new RuntimeSessionResolver(canonicalBindings);

  const steamSession: RuntimeSession = {
    connectionId: 'conn-steam-1556514',
    platformId: 'STEAM_CN',
    processId: 10100,
    playerId: 1556514,
    status: 'CONNECTED',
  };

  // Case 4a: TapTap session is omitted from active sessions
  const resultAfterDisconnect = resolver.resolve([steamSession], 'USER_SPECTATOR');

  assert(resultAfterDisconnect.mainSession !== null, 'Steam MAIN session remains active');
  assertStrictEqual(resultAfterDisconnect.mainSession?.role, 'MAIN', 'Steam MAIN role remains MAIN');
  assertStrictEqual(resultAfterDisconnect.mainSession?.playerId, 1556514, 'Steam MAIN playerId intact');
  assertStrictEqual(resultAfterDisconnect.selectedSpectatorSession, null, 'Selected spectator session is null');

  assert(
    resultAfterDisconnect.spectatorPrompt !== null,
    'Spectator prompt is present when TapTap is offline'
  );
  assert(
    resultAfterDisconnect.spectatorPrompt?.includes('TapTap') ?? false,
    'Spectator prompt mentions TapTap client'
  );
  assert(
    resultAfterDisconnect.spectatorPrompt?.includes('3045259') ?? false,
    'Spectator prompt mentions bound account 3045259'
  );
  assert(
    resultAfterDisconnect.spectatorPrompt?.includes('专用观战账号尚未启动') ?? false,
    'Spectator prompt indicates spectator account is not running / offline'
  );

  // Case 4b: TapTap session is present with status DISCONNECTED
  const tapDisconnectedSession: RuntimeSession = {
    connectionId: 'conn-taptap-3045259',
    platformId: 'TAPTAP',
    processId: 20200,
    playerId: 3045259,
    status: 'DISCONNECTED',
  };

  const resultWithExplicitDisconnect = resolver.resolve(
    [steamSession, tapDisconnectedSession],
    'USER_SPECTATOR'
  );
  assertStrictEqual(
    resultWithExplicitDisconnect.mainSession?.role,
    'MAIN',
    'Steam MAIN remains untouched when TapTap explicitly disconnected'
  );
  assertStrictEqual(
    resultWithExplicitDisconnect.selectedSpectatorSession,
    null,
    'Disconnected TapTap runtime is not selected'
  );
  assert(
    resultWithExplicitDisconnect.spectatorPrompt?.includes('TapTap') ?? false,
    'Prompt displays launch prompt for TapTap'
  );
});

// -----------------------------------------------------------------------------
// Test 5: Only Steam connected in USER_SPECTATOR mode -> shows prompt to launch TapTap, does not prompt logout
// -----------------------------------------------------------------------------
runTest('Test 5: Only Steam connected in USER_SPECTATOR mode -> shows prompt to launch TapTap, does not prompt logout', () => {
  console.log('\n--- Test 5: Only Steam connected in USER_SPECTATOR mode ---');
  const resolver = new RuntimeSessionResolver(canonicalBindings);

  const steamSession: RuntimeSession = {
    connectionId: 'conn-steam-1556514',
    platformId: 'STEAM_CN',
    processId: 10100,
    playerId: 1556514,
    status: 'CONNECTED',
  };

  const result = resolver.resolve([steamSession], 'USER_SPECTATOR');

  assert(
    result.spectatorPrompt !== null && result.spectatorPrompt.includes('专用观战账号尚未启动'),
    'Spectator prompt indicates dedicated spectator is offline'
  );
  assert(
    result.spectatorPrompt?.includes('3045259') ?? false,
    'Spectator prompt includes bound spectator PID 3045259'
  );

  // STRICT PROHIBITION AUDIT:
  // Strictly prohibit telling the user to log out of their Steam MAIN account!
  const forbiddenPhrases = [
    '登出',
    '退出当前账号',
    '退出主账号',
    '切换账号',
    '切换至已绑定的专用观战账号',
    '避免占用游戏对局',
    '请退出',
    'logout',
  ];

  for (const forbidden of forbiddenPhrases) {
    assert(
      !result.spectatorPrompt!.toLowerCase().includes(forbidden.toLowerCase()),
      `STRICT INVARIANT: Prompt does NOT contain prohibited logout phrase: "${forbidden}"`
    );
  }

  // hasRoleMismatch should NOT be true in dual-client mode when Steam MAIN is just running alongside
  assertStrictEqual(
    result.hasRoleMismatch,
    false,
    'hasRoleMismatch is false because Steam MAIN is a legitimate parallel client'
  );
});

// -----------------------------------------------------------------------------
// Test 6: Unknown client connected -> marked UNBOUND without overwriting bound clients
// -----------------------------------------------------------------------------
runTest('Test 6: Unknown client connected -> marked UNBOUND without overwriting bound clients', () => {
  console.log('\n--- Test 6: Unknown client connected -> marked UNBOUND without overwriting bound clients ---');
  const resolver = new RuntimeSessionResolver(canonicalBindings);

  const steamSession: RuntimeSession = {
    connectionId: 'conn-steam-1556514',
    platformId: 'STEAM_CN',
    processId: 10100,
    playerId: 1556514,
    status: 'CONNECTED',
  };

  const tapSession: RuntimeSession = {
    connectionId: 'conn-taptap-3045259',
    platformId: 'TAPTAP',
    processId: 20200,
    playerId: 3045259,
    status: 'CONNECTED',
  };

  const unknownSession: RuntimeSession = {
    connectionId: 'conn-unknown-bilibili-9999999',
    platformId: 'BILIBILI',
    processId: 30300,
    playerId: 9999999, // Unbound player ID on unconfigured platform
    displayName: 'UnknownGuest',
    status: 'CONNECTED',
  };

  const result = resolver.resolve(
    [steamSession, tapSession, unknownSession],
    'USER_SPECTATOR'
  );

  // 1. Unknown client is marked UNBOUND
  const resolvedUnknown = result.allSessions.find(
    (s) => s.connectionId === 'conn-unknown-bilibili-9999999'
  );
  assert(resolvedUnknown !== undefined, 'Unknown session is present in resolved sessions');
  assertStrictEqual(resolvedUnknown?.role, 'UNBOUND', 'Unknown session role is strictly UNBOUND');
  assertStrictEqual(resolvedUnknown?.resolvedRole, 'UNBOUND', 'resolvedRole alias is UNBOUND');
  assertStrictEqual(resolvedUnknown?.matchedBinding, null, 'Unknown session has null binding');

  // 2. Bound clients remain completely intact and unaltered
  assertStrictEqual(result.mainSession?.connectionId, steamSession.connectionId, 'MAIN session is intact');
  assertStrictEqual(result.mainSession?.role, 'MAIN', 'MAIN role is intact');
  assertStrictEqual(result.spectatorSession?.connectionId, tapSession.connectionId, 'USER_SPECTATOR session is intact');
  assertStrictEqual(result.spectatorSession?.role, 'USER_SPECTATOR', 'USER_SPECTATOR role is intact');
  assertStrictEqual(
    result.selectedSpectatorConnectionId,
    tapSession.connectionId,
    'TapTap remains the active spectator source despite unknown client'
  );
});

// -----------------------------------------------------------------------------
// Test 7: Promotion Test: PENDING_SPECTATOR promoted to exact USER_SPECTATOR on packet arrival
// -----------------------------------------------------------------------------
runTest('Test 7: Promotion Test: PENDING_SPECTATOR promoted to exact USER_SPECTATOR on packet arrival', () => {
  console.log('\n--- Test 7: Promotion Test: PENDING_SPECTATOR promoted on packet arrival ---');
  const resolver = new RuntimeSessionResolver(canonicalBindings);

  // Step 1: Handshake connected, playerId null
  const pendingSession: RuntimeSession = {
    connectionId: 'conn-taptap-3045259',
    platformId: 'TAPTAP',
    processId: 20200,
    playerId: null,
    status: 'CONNECTED',
  };

  const resolvedStep1 = resolver.resolveSession(pendingSession);
  assertStrictEqual(resolvedStep1.role, 'PENDING_SPECTATOR', 'Step 1: Role is PENDING_SPECTATOR');

  // Step 2: PlayerId packet arrives with 3045259
  const packetArrivedSession: RuntimeSession = {
    ...pendingSession,
    playerId: 3045259,
  };

  const resolvedStep2 = resolver.resolveSession(packetArrivedSession);
  assertStrictEqual(resolvedStep2.role, 'USER_SPECTATOR', 'Step 2: Immediately promoted to USER_SPECTATOR');
  assertStrictEqual(resolvedStep2.matchedBinding?.role, 'USER_SPECTATOR', 'Step 2: Matched binding role is USER_SPECTATOR');

  // Step 3: Unmatched PID arrives on TapTap (e.g., user logged into wrong account 7777777 on TapTap)
  const wrongAccountSession: RuntimeSession = {
    ...pendingSession,
    playerId: 7777777,
  };

  const resolvedStep3 = resolver.resolveSession(wrongAccountSession);
  assertStrictEqual(resolvedStep3.role, 'UNBOUND', 'Step 3: Wrong PID on TapTap resolves to UNBOUND');
  assertStrictEqual(resolvedStep3.matchedBinding, null, 'Step 3: Matched binding is null for wrong PID');
});

// -----------------------------------------------------------------------------
// Test 8: Zero-Default Production Invariant & Role Enum Integrity
// -----------------------------------------------------------------------------
runTest('Test 8: Zero-Default Production Invariant & Role Enum Integrity', () => {
  console.log('\n--- Test 8: Zero-Default Production Invariant & Role Enum Integrity ---');
  // 1. Zero hardcoded accounts in DEFAULT_CANONICAL_BINDINGS
  assertStrictEqual(DEFAULT_CANONICAL_BINDINGS.length, 0, 'Production DEFAULT_CANONICAL_BINDINGS is strictly empty');

  // 2. Default resolver initialized without custom bindings is unconfigured
  const defaultResolver = new RuntimeSessionResolver();
  const defaultBindings = defaultResolver.getBindings();
  assertStrictEqual(defaultBindings.length, 0, 'Default resolver has 0 bindings when unconfigured');

  // 3. Unconfigured resolver leaves sessions UNBOUND and prompts user to configure
  const unconfiguredRes = defaultResolver.resolve([
    {
      connectionId: 'conn-steam-1',
      platformId: 'STEAM_CN',
      processId: 1001,
      playerId: 1556514,
      status: 'CONNECTED',
    },
    {
      connectionId: 'conn-tap-1',
      platformId: 'TAPTAP',
      processId: 2002,
      playerId: 3045259,
      status: 'CONNECTED',
    },
  ], 'USER_SPECTATOR');

  assertStrictEqual(unconfiguredRes.mainSession, null, 'Unconfigured MAIN binding resolves mainSession to null');
  assertStrictEqual(unconfiguredRes.spectatorSession, null, 'Unconfigured USER_SPECTATOR binding resolves spectatorSession to null');
  assert(unconfiguredRes.allSessions.every((s) => s.role === 'UNBOUND'), 'All sessions resolve to UNBOUND when unconfigured');
  assertStrictEqual(
    unconfiguredRes.spectatorPrompt,
    '专用观战账号未配置。请在账号中心绑定观战账号和平台',
    'Spectator prompt instructs user to configure account in account center'
  );

  // 4. Role Enum Integrity
  assertStrictEqual(ResolvedRuntimeRole.MAIN, 'MAIN', 'ResolvedRuntimeRole.MAIN is MAIN');
  assertStrictEqual(ResolvedRuntimeRole.USER_SPECTATOR, 'USER_SPECTATOR', 'ResolvedRuntimeRole.USER_SPECTATOR is USER_SPECTATOR');
  assertStrictEqual(ResolvedRuntimeRole.PENDING_SPECTATOR, 'PENDING_SPECTATOR', 'ResolvedRuntimeRole.PENDING_SPECTATOR is PENDING_SPECTATOR');
  assertStrictEqual(ResolvedRuntimeRole.PENDING_MAIN, 'PENDING_MAIN', 'ResolvedRuntimeRole.PENDING_MAIN is PENDING_MAIN');
  assertStrictEqual(ResolvedRuntimeRole.UNBOUND, 'UNBOUND', 'ResolvedRuntimeRole.UNBOUND is UNBOUND');
});

// -----------------------------------------------------------------------------
// Test 9: role="MAIN" claimed from TapTap HELLO must not override USER_SPECTATOR binding
// -----------------------------------------------------------------------------
runTest('Test 9: role="MAIN" claimed from TapTap HELLO must not override USER_SPECTATOR binding', () => {
  console.log('\n--- Test 9: role="MAIN" claimed from TapTap HELLO must not override USER_SPECTATOR binding ---');
  const resolver = new RuntimeSessionResolver(canonicalBindings);

  const tapSessionClaimingMain: RuntimeSession = {
    connectionId: 'conn-taptap-spoof',
    platformId: 'TAPTAP',
    processId: 20200,
    playerId: 3045259,
    displayName: 'TapTapClient',
    status: 'CONNECTED',
    role: 'MAIN', // Claimed role from client HELLO
  };

  const resolved = resolver.resolveSession(tapSessionClaimingMain);
  assertStrictEqual(
    resolved.role,
    'USER_SPECTATOR',
    'Resolver enforces USER_SPECTATOR binding, ignoring client role="MAIN" claim'
  );
  assertStrictEqual(
    resolved.resolvedRole,
    'USER_SPECTATOR',
    'resolvedRole is strictly USER_SPECTATOR'
  );
  assert(resolved.matchedBinding !== null, 'Matched binding exists');
  assertStrictEqual(resolved.matchedBinding?.role, 'USER_SPECTATOR', 'Binding role is USER_SPECTATOR');
});

console.log('================================================================================');
console.log(`RUNTIME SESSION RESOLVER TEST RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
console.log('================================================================================');

if (failCount > 0) {
  process.exit(1);
}
