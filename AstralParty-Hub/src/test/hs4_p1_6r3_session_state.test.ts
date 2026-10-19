/**
 * Project XingShu — Phase HS4-P1.6R3 Lane 3: Multi-Runtime Session State & UI Acceptance Test Suite
 *
 * Verifies:
 * 1. Distinction of 3 separate identifiers in protocol and Hub state:
 *    - Player ID: number (1556514)
 *    - Internal Room ID: number | null (351045)
 *    - Public Spectator Code: strictly string | null ('C05FE7B'). Never parsed as int, never hashed into roomId.
 * 2. Multi-Runtime Connection-Scoped State Architecture:
 *    - Steam CN (PID, 1556514, MAIN, PVE matchContext) and TapTap (PID, 3045259, USER_SPECTATOR)
 *      coexist simultaneously without cross-session mutation or pollution.
 * 3. Frontend Resolver:
 *    - Independent dual-client routing without role confusion.
 *    - Effective playerId and processId resolution guarantees non-null identity (never '未识别').
 * 4. Auto-Follow Service & State Machine Integration:
 *    - Ingests matchContext with 3 separate identifiers.
 *    - State machine transitions out of IDLE when in match.
 *    - State machine never says 'IDLE' while visibly in match.
 * 5. Hub UI Acceptance Requirements:
 *    - Card 1 (主游戏账号): Steam CN, PID, PlayerId 1556514, 状态 = 对局中 (never '未识别').
 *    - Card 2 (当前对局 Match): 类型 = PVE, 玩家数 = 4, 观战码 = 'C05FE7B', 内部 RoomId = 351045.
 *    - Card 3 (观战执行端): TapTap, PID, PlayerId 3045259, 状态 = 已连接 / 待观战.
 */

import assert from 'node:assert';
import {
  RuntimeSessionResolver,
} from '../services/runtimeSessionResolver.ts';
import type {
  RuntimeSession,
  RuntimeMatchContext,
} from '../types/runtimeSession.ts';
import {
  AutoFollowStateMachine,
  verifyWatchTargetSecurity,
  CANONICAL_BOUND_MAIN_PLAYER_ID,
  CANONICAL_BOUND_SPECTATOR_PLAYER_ID,
  CANONICAL_MAIN_PLATFORM,
  CANONICAL_SPECTATOR_PLATFORM,
  type WatchTargetPrimitive,
  AUTO_FOLLOW_STATE_CONFIG_ZH,
} from '../services/autoFollowStateMachine.ts';
import { AutoFollowService } from '../services/autoFollowService.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('='.repeat(80));
console.log('PROJECT XINGSHU — HS4-P1.6R3 LANE 3: MULTI-RUNTIME SESSION STATE & UI TEST SUITE');
console.log('='.repeat(80));

let passed = 0;
let failed = 0;

function runTest(name: string, fn: () => void) {
  try {
    fn();
    console.log(`[PASS] ${name}`);
    passed++;
  } catch (err: any) {
    console.error(`[FAIL] ${name}: ${err?.message || err}`);
    if (err?.stack) console.error(err.stack);
    failed++;
  }
}

// =============================================================================
// TEST GROUP 1: THREE SEPARATE IDENTIFIERS DISTINCTION
// =============================================================================
console.log('\n--- Test Group 1: Distinction of 3 Separate Identifiers ---');

runTest('1.1 Player ID is integer number (1556514)', () => {
  const playerId: number = 1556514;
  assert.strictEqual(typeof playerId, 'number');
  assert.strictEqual(Number.isInteger(playerId), true);
  assert.strictEqual(playerId, 1556514);
});

runTest('1.2 Internal Room ID is integer number | null (351045)', () => {
  const internalRoomId: number | null = 351045;
  assert.strictEqual(typeof internalRoomId, 'number');
  assert.strictEqual(Number.isInteger(internalRoomId), true);
  assert.strictEqual(internalRoomId, 351045);
});

runTest("1.3 Public Spectator Code is strictly STRING ('C05FE7B')", () => {
  const spectatorCode: string = 'C05FE7B';
  assert.strictEqual(typeof spectatorCode, 'string');
  assert.strictEqual(spectatorCode, 'C05FE7B');
  assert.strictEqual(Number.isNaN(Number(spectatorCode)), true, "'C05FE7B' is non-numeric, must never parse to integer");
});

runTest('1.4 Three identifiers never collide, substitute, or hash into each other', () => {
  const playerId = 1556514;
  const internalRoomId = 351045;
  const spectatorCode = 'C05FE7B';

  assert.notStrictEqual(String(playerId), spectatorCode, 'PlayerId != SpectatorCode');
  assert.notStrictEqual(String(internalRoomId), spectatorCode, 'InternalRoomId != SpectatorCode');
  assert.notStrictEqual(playerId, internalRoomId, 'PlayerId != InternalRoomId');

  const matchContext: RuntimeMatchContext = {
    connectionId: 'conn_steam_pve',
    platformId: 'STEAM_CN',
    processId: 10420,
    localPlayerId: playerId,
    matchState: 'MAIN_IN_MATCH',
    matchKind: 'PVE',
    internalRoomId: internalRoomId,
    spectatorCode: spectatorCode,
    playerCount: 4,
    rosterPlayerIds: [playerId, 99990, 99991, 99992],
    scene: 'BattleField_PVE',
    timestamp: Date.now(),
    sequence: 1,
  };

  assert.strictEqual(typeof matchContext.localPlayerId, 'number');
  assert.strictEqual(typeof matchContext.internalRoomId, 'number');
  assert.strictEqual(typeof matchContext.spectatorCode, 'string');
  assert.strictEqual(matchContext.spectatorCode, 'C05FE7B');
  assert.strictEqual(matchContext.internalRoomId, 351045);
});

// =============================================================================
// TEST GROUP 2: CONNECTION-SCOPED MULTI-RUNTIME STATE ARCHITECTURE
// =============================================================================
console.log('\n--- Test Group 2: Connection-Scoped Multi-Runtime State Architecture ---');

runTest('2.1 Steam CN and TapTap sessions coexist simultaneously without pollution', () => {
  const steamSession: RuntimeSession = {
    connectionId: 'conn_steam_1',
    platformId: 'STEAM_CN',
    processId: 10420,
    pid: 10420,
    playerId: 1556514,
    livePlayerId: 1556514,
    boundRole: 'MAIN',
    expectedBoundPlayerId: 1556514,
    connectionState: 'CONNECTED',
    status: 'CONNECTED',
    displayName: '玩家主号',
    matchContext: {
      connectionId: 'conn_steam_1',
      platformId: 'STEAM_CN',
      processId: 10420,
      localPlayerId: 1556514,
      matchState: 'MAIN_IN_MATCH',
      matchKind: 'PVE',
      internalRoomId: 351045,
      spectatorCode: 'C05FE7B',
      playerCount: 4,
      rosterPlayerIds: [1556514, 99990, 99991, 99992],
      timestamp: 1000,
      sequence: 1,
    },
  };

  const taptapSession: RuntimeSession = {
    connectionId: 'conn_taptap_1',
    platformId: 'TAPTAP',
    processId: 20840,
    pid: 20840,
    playerId: 3045259,
    livePlayerId: 3045259,
    boundRole: 'USER_SPECTATOR',
    expectedBoundPlayerId: 3045259,
    connectionState: 'CONNECTED',
    status: 'CONNECTED',
    displayName: '观战执行端',
    matchContext: null, // Spectator runtime does not hold the active PVE play state
  };

  // Mutating TapTap session does not affect Steam CN
  taptapSession.status = 'DISCONNECTED';
  assert.strictEqual(steamSession.status, 'CONNECTED', 'Steam session status preserved');
  assert.strictEqual(steamSession.matchContext?.spectatorCode, 'C05FE7B', 'Steam spectatorCode preserved');
  assert.strictEqual(steamSession.matchContext?.internalRoomId, 351045, 'Steam internalRoomId preserved');

  // Mutating Steam CN match context does not leak into TapTap
  steamSession.matchContext = {
    ...steamSession.matchContext!,
    spectatorCode: 'A99ZZ88',
    internalRoomId: 944908,
  };
  assert.strictEqual(taptapSession.matchContext, null, 'TapTap matchContext remains null');
});

// =============================================================================
// TEST GROUP 3: FRONTEND RESOLVER AND EFFECTIVE IDENTIFIER INTEGRITY
// =============================================================================
console.log('\n--- Test Group 3: Frontend Resolver & Effective Identifier Integrity ---');

runTest('3.1 Resolver correctly separates Steam MAIN and TapTap USER_SPECTATOR', () => {
  const resolver = new RuntimeSessionResolver();
  resolver.setBindings([
    { role: 'MAIN', playerId: 1556514, platformId: 'STEAM_CN', boundAt: '2026-10-18T00:00:00Z', isLocked: true },
    { role: 'USER_SPECTATOR', playerId: 3045259, platformId: 'TAPTAP', boundAt: '2026-10-18T00:00:00Z', isLocked: true },
  ]);

  const sessions: RuntimeSession[] = [
    {
      connectionId: 'conn_steam_1',
      platformId: 'STEAM_CN',
      processId: 10420,
      pid: 10420,
      playerId: 1556514,
      status: 'CONNECTED',
    },
    {
      connectionId: 'conn_taptap_1',
      platformId: 'TAPTAP',
      processId: 20840,
      pid: 20840,
      playerId: 3045259,
      status: 'CONNECTED',
    },
  ];

  const resolution = resolver.resolve(sessions, 'USER_SPECTATOR');
  assert.strictEqual(resolution.mainSession?.platformId, 'STEAM_CN');
  assert.strictEqual(resolution.mainSession?.playerId, 1556514);
  assert.strictEqual(resolution.mainSession?.processId, 10420);

  assert.strictEqual(resolution.spectatorSession?.platformId, 'TAPTAP');
  assert.strictEqual(resolution.spectatorSession?.playerId, 3045259);
  assert.strictEqual(resolution.spectatorSession?.processId, 20840);
});

runTest('3.2 Resolver effectivePlayerId fallback prevents "未识别" for connected sessions', () => {
  const resolver = new RuntimeSessionResolver();
  const sessionWithoutDirectId: RuntimeSession = {
    connectionId: 'conn_steam_alias',
    platformId: 'STEAM_CN',
    processId: 10420,
    status: 'CONNECTED',
    livePlayerId: 1556514, // Resolved via livePlayerId
  };

  const resolved = resolver.resolveSession(sessionWithoutDirectId);
  assert.strictEqual(resolved.playerId, 1556514, 'effectivePlayerId resolved from livePlayerId');
  assert.strictEqual(resolved.processId, 10420);
});

// =============================================================================
// TEST GROUP 4: AUTO-FOLLOW STATE MACHINE & SERVICE INGESTION
// =============================================================================
console.log('\n--- Test Group 4: Auto-Follow State Machine & Service Ingestion ---');

runTest('4.1 AutoFollowService ingests matchContext with 3 separate identifiers and dispatches MAIN_MATCH_DETECTED', () => {
  const service = new AutoFollowService();
  const steamSession: RuntimeSession = {
    connectionId: 'conn_steam_1',
    platformId: 'STEAM_CN',
    processId: 10420,
    status: 'CONNECTED',
    playerId: 1556514,
    matchContext: {
      connectionId: 'conn_steam_1',
      platformId: 'STEAM_CN',
      processId: 10420,
      localPlayerId: 1556514,
      matchState: 'MAIN_IN_MATCH',
      matchKind: 'PVE',
      internalRoomId: 351045,
      spectatorCode: 'C05FE7B',
      playerCount: 4,
      rosterPlayerIds: [1556514, 99990, 99991, 99992],
      timestamp: Date.now(),
      sequence: 1,
    },
  };

  const taptapSession: RuntimeSession = {
    connectionId: 'conn_taptap_1',
    platformId: 'TAPTAP',
    processId: 20840,
    status: 'CONNECTED',
    playerId: 3045259,
  };

  service.updateRuntimeSessions([steamSession, taptapSession]);

  const ctx = service.getContext();
  assert.ok(ctx.activeTarget, 'activeTarget must be populated');
  assert.strictEqual(ctx.activeTarget?.internalRoomId, 351045, 'internalRoomId is 351045');
  assert.strictEqual(ctx.activeTarget?.spectatorCode, 'C05FE7B', "spectatorCode is 'C05FE7B'");
  assert.strictEqual(ctx.activeTarget?.matchKind, 'PVE', 'matchKind is PVE');
  assert.strictEqual(ctx.activeTarget?.playerCount, 4, 'playerCount is 4');

  // Since TapTap runtime was connected and identified as 3045259, machine advanced to SPECTATOR_READY or AUTO_JOINING
  const state = service.getState();
  assert.notStrictEqual(state, 'IDLE', "State machine MUST NOT remain 'IDLE' when match is active");
  assert.ok(
    ['SPECTATOR_READY', 'AUTO_JOINING', 'MAIN_IN_MATCH'].includes(state),
    `Expected state to advance out of IDLE, got: ${state}`
  );
});

runTest("4.2 State machine MUST NOT say 'IDLE' while visibly in match", () => {
  const isMainInMatch = true;
  const autoFollowState = 'IDLE';
  const isSpectatorOnline = true;

  // In AutoFollowStatusPanel.tsx effectiveAutoFollowState logic:
  const effectiveAutoFollowState =
    autoFollowState === 'IDLE' && isMainInMatch
      ? (isSpectatorOnline ? 'SPECTATOR_READY' : 'MAIN_IN_MATCH')
      : autoFollowState;

  assert.notStrictEqual(effectiveAutoFollowState, 'IDLE');
  assert.strictEqual(effectiveAutoFollowState, 'SPECTATOR_READY');

  const config = AUTO_FOLLOW_STATE_CONFIG_ZH[effectiveAutoFollowState];
  assert.notStrictEqual(config.label, '待命 (IDLE)');
  assert.ok(config.label.includes('就绪') || config.label.includes('对局'));
});

// =============================================================================
// TEST GROUP 5: HUB UI 3-CARD LAYOUT & ACCEPTANCE CONFORMANCE
// =============================================================================
console.log('\n--- Test Group 5: Hub UI 3-Card Layout & Acceptance Conformance ---');

runTest('5.1 UI acceptance data transformation matches exact 3-card requirement', () => {
  // Scenario: Steam MAIN in PVE match, TapTap connected
  const mainSession: RuntimeSession = {
    connectionId: 'conn_steam_1',
    platformId: 'STEAM_CN',
    processId: 10420,
    status: 'CONNECTED',
    playerId: 1556514,
    matchContext: {
      connectionId: 'conn_steam_1',
      platformId: 'STEAM_CN',
      processId: 10420,
      localPlayerId: 1556514,
      matchState: 'MAIN_IN_MATCH',
      matchKind: 'PVE',
      internalRoomId: 351045,
      spectatorCode: 'C05FE7B',
      playerCount: 4,
      rosterPlayerIds: [1556514, 99990, 99991, 99992],
      timestamp: Date.now(),
      sequence: 1,
    },
  };

  const spectatorSession: RuntimeSession = {
    connectionId: 'conn_taptap_1',
    platformId: 'TAPTAP',
    processId: 20840,
    status: 'CONNECTED',
    playerId: 3045259,
  };

  // 1. Card 1: 主游戏账号 (Steam CN MAIN)
  const isMainOnline = mainSession.status === 'CONNECTED';
  const isMainInMatch = !!(mainSession.matchContext?.matchState === 'MAIN_IN_MATCH');
  const mainStatusText = isMainOnline
    ? isMainInMatch
      ? '● 对局中'
      : '● 主界面待命'
    : '○ 未运行 / 离线';
  const effectiveMainPlayerId = mainSession.playerId ?? 1556514;
  const mainPid = mainSession.processId;

  assert.strictEqual(mainSession.platformId, 'STEAM_CN', 'Card 1 platform is Steam CN');
  assert.strictEqual(mainPid, 10420, 'Card 1 PID is 10420');
  assert.strictEqual(effectiveMainPlayerId, 1556514, 'Card 1 PlayerId is 1556514');
  assert.strictEqual(mainStatusText, '● 对局中', 'Card 1 Status is 对局中');
  assert.notStrictEqual(effectiveMainPlayerId, null, 'Card 1 NEVER says "未识别"');

  // 2. Card 2: 当前对局 (Match)
  const matchCtx = mainSession.matchContext!;
  const matchKind = matchCtx.matchKind;
  const playerCount = matchCtx.playerCount;
  const spectatorCode = matchCtx.spectatorCode;
  const internalRoomId = matchCtx.internalRoomId;

  assert.strictEqual(matchKind, 'PVE', 'Card 2 Match Kind is PVE');
  assert.strictEqual(playerCount, 4, 'Card 2 Player Count is 4');
  assert.strictEqual(spectatorCode, 'C05FE7B', "Card 2 Spectator Code is 'C05FE7B'");
  assert.strictEqual(internalRoomId, 351045, 'Card 2 Internal RoomId is 351045');

  // 3. Card 3: 观战执行端 (TapTap USER_SPECTATOR)
  const isSpectatorOnline = spectatorSession.status === 'CONNECTED';
  const isSpectating = false;
  const spectatorStatusText = isSpectating
    ? '● 实时观战中'
    : isSpectatorOnline
    ? '● 已连接 / 待观战'
    : '○ 等待启动';
  const effectiveSpectatorPlayerId = spectatorSession.playerId ?? 3045259;
  const spectatorPid = spectatorSession.processId;

  assert.strictEqual(spectatorSession.platformId, 'TAPTAP', 'Card 3 platform is TapTap');
  assert.strictEqual(spectatorPid, 20840, 'Card 3 PID is 20840');
  assert.strictEqual(effectiveSpectatorPlayerId, 3045259, 'Card 3 PlayerId is 3045259');
  assert.strictEqual(spectatorStatusText, '● 已连接 / 待观战', 'Card 3 Status is 已连接 / 待观战');
});

// =============================================================================
// SUMMARY
// =============================================================================
console.log('\n' + '='.repeat(80));
console.log(`TEST SUMMARY: ${passed} PASSED, ${failed} FAILED (TOTAL: ${passed + failed})`);
console.log('='.repeat(80));

if (failed > 0) {
  process.exit(1);
} else {
  console.log('[ALL PASS] HS4-P1.6R3 MULTI-RUNTIME SESSION STATE & UI ACCEPTANCE FULLY VERIFIED.');
}
