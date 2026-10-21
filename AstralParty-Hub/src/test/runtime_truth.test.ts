/**
 * Project XingShu — HS4-P1.3B Runtime Truth Conformance Suite
 * Verifies Semantic Truth in "星枢运行时" (RuntimePage.tsx)
 *
 * Core Architecture Invariants:
 * 1. selectedRuntimePlan: User-configured plan/version
 * 2. installedRuntime: Physical runtime on disk (detected via container hash / manifest)
 * 3. activeRuntimeSession: Confirmed ONLY via live TCP Bridge handshake (HELLO_ACK, gamePid, runtimeVersion)
 *
 * Assertions:
 * - Test 1: Plan selected but game not running -> status is "已就绪 / 游戏未运行", NOT "正在运行".
 * - Test 2: Game running but no handshake -> status is "等待 Runtime 连接", NOT "正在运行".
 * - Test 3: Game running with valid handshake -> status is "当前会话活跃 (PID)".
 * - Test 4: Legacy runtime condition -> status shows legacy warning.
 * - Test 5: Static codebase audit of RuntimePage.tsx verifying truth invariants.
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  deriveRuntimeTruthState,
  getVersionItemBadge,
  getBridgeStateDisplay,
  getGameProcessDisplay,
  CANONICAL_RUNTIME_HOST,
  CANONICAL_TRANSPORT,
  HANDSHAKE_HELLO_ACK,
  HANDSHAKE_HELLO_PENDING,
  HANDSHAKE_DISCONNECTED,
  HANDSHAKE_LEGACY_DETECTED,
  STATUS_READY_NOT_RUNNING_ZH,
  STATUS_WAITING_CONNECTION_ZH,
  STATUS_LEGACY_WARNING_ZH,
} from '../pages/runtime/runtimeTruth.ts';
import type {
  RuntimeVersion,
  InstalledRuntime,
  GameProcessStatus,
  BridgeConnectionState,
} from '../shared/types/index.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.3B: RUNTIME MANAGER TRUTH CONFORMANCE SUITE');
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
    console.error(`[TEST_EXCEPTION] ${name}:`, err.message);
  }
}

const mockVersions: RuntimeVersion[] = [
  {
    version_id: 'xingshu-1.3.4',
    runtime_type: 'XingShu',
    version_name: 'XingShu v1.3.4 (Production Core)',
    is_installed: true,
    is_active: true,
    install_date: '2026-10-15',
    description: '针对星引擎深度优化的原生 DirectX 11 注入通道与微秒级环回 TCP 通信层',
  },
  {
    version_id: 'bep-5.4.23',
    runtime_type: 'BEPINEX_5',
    version_name: 'BepInEx 5.4.23.2 (Bleeding Edge)',
    is_installed: true,
    is_active: false,
    install_date: '2026-10-10',
    description: '标准 Unity IL2CPP / Mono 互操作加载器与 Harmony 补丁框架',
  },
  {
    version_id: 'vanilla-official',
    runtime_type: 'VANILLA',
    version_name: '原生纯净版 (官方未修改)',
    is_installed: true,
    is_active: false,
    description: '官方原生游戏未注入二进制文件',
  },
];

const mockInstalledRuntime: InstalledRuntime = {
  version_id: 'xingshu-1.3.4',
  runtime_type: 'XingShu',
  version_name: 'XingShu v1.3.4 (Production Core)',
  install_path: 'com.unity.addressables/.../__data',
  container_hash: 'sha256:7f8e219ba420d931acbf6e',
  manifest_verified: true,
  installed_at: '2026-10-15T10:00:00Z',
};

// -----------------------------------------------------------------------------
// Test 1: Plan selected but game not running -> status is "已就绪 / 游戏未运行", NOT "正在运行"
// -----------------------------------------------------------------------------
console.log('\n--- Test 1: Plan selected but game not running ---');

runTest('Test 1: Status is "已就绪 / 游戏未运行" when game is closed', () => {
  const gameProcess: GameProcessStatus = {
    isRunning: false,
    pid: null,
    processName: 'AstralParty_CN.exe',
    platformId: 'STEAM_CN',
  };

  const bridgeStatus: Partial<BridgeConnectionState> = {
    isRunning: true,
    runtimeConnected: false,
    tcpConnected: false,
    namedPipeConnected: false,
    gamePid: undefined,
  };

  const state = deriveRuntimeTruthState({
    selectedVersionId: 'xingshu-1.3.4',
    versions: mockVersions,
    installedRuntime: mockInstalledRuntime,
    gameProcess,
    bridgeStatus: bridgeStatus as BridgeConnectionState,
  });

  assertStrictEqual(state.status_category, 'READY_NOT_RUNNING', 'status_category === READY_NOT_RUNNING');
  assertStrictEqual(state.overall_status_display, STATUS_READY_NOT_RUNNING_ZH, `overall_status_display === "${STATUS_READY_NOT_RUNNING_ZH}"`);
  assert(!state.overall_status_display.includes('正在运行'), 'Status does NOT contain "正在运行"');
  assertStrictEqual(state.active_session.is_live_active, false, 'active_session.is_live_active is strictly false');
  assertStrictEqual(state.active_session.game_running, false, 'active_session.game_running is false');
  assertStrictEqual(state.active_session.game_pid, null, 'active_session.game_pid is null');
  assertStrictEqual(state.active_session.handshake_status, HANDSHAKE_DISCONNECTED, 'handshake_status is DISCONNECTED');

  // Verify version matrix badges
  const badgeXingShu = getVersionItemBadge(mockVersions[0], state, true);
  assertStrictEqual(badgeXingShu.label, '【已就绪】', 'Selected plan badge is 【已就绪】');
  assertStrictEqual(badgeXingShu.isLiveActive, false, 'badgeXingShu.isLiveActive is false');
  assert(!badgeXingShu.label.includes('正在运行'), 'Selected plan badge strictly does NOT say 正在运行');

  const badgeBep = getVersionItemBadge(mockVersions[1], state, true);
  assertStrictEqual(badgeBep.label, '【已安装】', 'Unselected installed badge is 【已安装】');
  assert(!badgeBep.label.includes('正在运行'), 'Unselected badge strictly does NOT say 正在运行');

  // Verify bridge and game process display helpers
  const bridgeDisp1 = getBridgeStateDisplay(state.bridge_state, true);
  assertStrictEqual(bridgeDisp1.text, '等待连接', 'Test 1: Bridge display text is 等待连接');

  const procDisp1 = getGameProcessDisplay(state.active_session.game_running, state.active_session.game_pid, true);
  assertStrictEqual(procDisp1.text, '未运行', 'Test 1: Process display text is 未运行');
});

// -----------------------------------------------------------------------------
// Test 2: Game running but no handshake -> status is "等待 Runtime 连接", NOT "正在运行"
// -----------------------------------------------------------------------------
console.log('\n--- Test 2: Game running but no handshake ---');

runTest('Test 2: Status is "等待 Runtime 连接" when game running without handshake', () => {
  const gameProcess: GameProcessStatus = {
    isRunning: true,
    pid: 8848,
    processName: 'AstralParty_CN.exe',
    platformId: 'STEAM_CN',
  };

  const bridgeStatus: Partial<BridgeConnectionState> = {
    isRunning: true,
    runtimeConnected: false,
    tcpConnected: false,
    namedPipeConnected: false,
    gamePid: undefined,
    runtimeVersion: undefined,
  };

  const state = deriveRuntimeTruthState({
    selectedVersionId: 'xingshu-1.3.4',
    versions: mockVersions,
    installedRuntime: mockInstalledRuntime,
    gameProcess,
    bridgeStatus: bridgeStatus as BridgeConnectionState,
  });

  assertStrictEqual(state.status_category, 'WAITING_RUNTIME_CONNECTION', 'status_category === WAITING_RUNTIME_CONNECTION');
  assert(state.overall_status_display.includes('等待 Runtime 连接'), 'Status text contains "等待 Runtime 连接"');
  assertStrictEqual(state.overall_status_display, STATUS_WAITING_CONNECTION_ZH, `overall_status_display === "${STATUS_WAITING_CONNECTION_ZH}"`);
  assert(!state.overall_status_display.includes('正在运行'), 'Status does NOT say "正在运行"');
  assertStrictEqual(state.active_session.is_live_active, false, 'active_session.is_live_active is false');
  assertStrictEqual(state.active_session.game_running, true, 'active_session.game_running is true');
  assertStrictEqual(state.active_session.game_pid, 8848, 'active_session.game_pid is 8848');
  assertStrictEqual(state.active_session.handshake_status, HANDSHAKE_HELLO_PENDING, 'handshake_status is HELLO_PENDING');

  // Verify version badge
  const badgeXingShu = getVersionItemBadge(mockVersions[0], state, true);
  assertStrictEqual(badgeXingShu.label, '【已安装 / 等待连接】', 'Selected plan badge shows 【已安装 / 等待连接】');
  assertStrictEqual(badgeXingShu.isLiveActive, false, 'isLiveActive is false');
  assert(!badgeXingShu.label.includes('正在运行'), 'Selected plan badge does NOT say 正在运行');
});

// -----------------------------------------------------------------------------
// Test 3: Game running with valid handshake -> status is "当前会话活跃 (PID)"
// -----------------------------------------------------------------------------
console.log('\n--- Test 3: Game running with valid handshake ---');

runTest('Test 3: Status is "当前会话活跃 (PID)" when game running with verified handshake', () => {
  const gameProcess: GameProcessStatus = {
    isRunning: true,
    pid: 9527,
    processName: 'AstralParty_CN.exe',
    platformId: 'STEAM_CN',
  };

  const bridgeStatus: Partial<BridgeConnectionState> = {
    isRunning: true,
    runtimeConnected: true,
    tcpConnected: true,
    protocolVersion: '0.1',
    gamePid: 9527,
    runtimeVersion: 'xingshu-1.3.4',
    lastHeartbeatTimestamp: Date.now(),
    lastHeartbeatLatencyMs: 0.35,
    activeTransport: 'LOOPBACK_TCP',
  };

  const state = deriveRuntimeTruthState({
    selectedVersionId: 'xingshu-1.3.4',
    versions: mockVersions,
    installedRuntime: mockInstalledRuntime,
    gameProcess,
    bridgeStatus: bridgeStatus as BridgeConnectionState,
  });

  assertStrictEqual(state.status_category, 'LIVE_SESSION_ACTIVE', 'status_category === LIVE_SESSION_ACTIVE');
  assertStrictEqual(state.active_session.is_live_active, true, 'active_session.is_live_active is confirmed true');
  assertStrictEqual(state.active_session.game_pid, 9527, 'active_session.game_pid is 9527');
  assertStrictEqual(state.active_session.handshake_status, HANDSHAKE_HELLO_ACK, 'handshake_status is HELLO_ACK');
  assertStrictEqual(state.active_session.component, CANONICAL_RUNTIME_HOST, 'Host is XingShuRuntimeHost');
  assertStrictEqual(state.active_session.active_transport, CANONICAL_TRANSPORT, 'Transport is Loopback TCP');
  assertStrictEqual(state.overall_status_display, '当前会话活跃 (PID: 9527)', 'overall_status_display contains PID 9527');

  // Verify version badge
  const badgeXingShu = getVersionItemBadge(mockVersions[0], state, true);
  assertStrictEqual(badgeXingShu.label, '【当前会话活跃 (PID: 9527)】', 'Active version row displays 【当前会话活跃 (PID: 9527)】');
  assertStrictEqual(badgeXingShu.isLiveActive, true, 'badgeXingShu.isLiveActive is true');
  assertStrictEqual(badgeXingShu.badgeClass, 'badge badge-success', 'Badge is badge-success');

  // Verify bridge and game process display helpers
  const bridgeDisp3 = getBridgeStateDisplay(state.bridge_state, true);
  assertStrictEqual(bridgeDisp3.text, '协议已就绪', 'Test 3: Bridge display text is 协议已就绪');

  const procDisp3 = getGameProcessDisplay(state.active_session.game_running, state.active_session.game_pid, true);
  assertStrictEqual(procDisp3.text, '运行中', 'Test 3: Process display text is 运行中');
  assertStrictEqual(procDisp3.detail, 'PID: 9527', 'Test 3: Process detail is PID: 9527');
});

// -----------------------------------------------------------------------------
// Test 4: Legacy runtime condition -> status shows legacy warning
// -----------------------------------------------------------------------------
console.log('\n--- Test 4: Legacy runtime condition warning ---');

runTest('Test 4: Legacy RC1.1 condition displays legacy warning', () => {
  const gameProcess: GameProcessStatus = {
    isRunning: true,
    pid: 10001,
    processName: 'AstralParty_CN.exe',
    platformId: 'STEAM_CN',
  };

  // Case 4A: Explicit legacy detected
  const bridgeStatusLegacyA: Partial<BridgeConnectionState> = {
    isRunning: true,
    runtimeConnected: false,
    tcpConnected: false,
    namedPipeConnected: false,
  };

  const stateA = deriveRuntimeTruthState({
    selectedVersionId: 'xingshu-1.3.4',
    versions: mockVersions,
    installedRuntime: mockInstalledRuntime,
    gameProcess,
    bridgeStatus: bridgeStatusLegacyA as BridgeConnectionState,
    options: { legacyDetected: true },
  });

  assertStrictEqual(stateA.status_category, 'LEGACY_WARNING', 'Case 4A status_category === LEGACY_WARNING');
  assertStrictEqual(stateA.legacy_detected, true, 'Case 4A legacy_detected === true');
  assertStrictEqual(stateA.active_session.is_live_active, false, 'Case 4A is_live_active is strictly false');
  assertStrictEqual(stateA.active_session.handshake_status, HANDSHAKE_LEGACY_DETECTED, 'Case 4A handshake_status is LEGACY_DETECTED');
  assertStrictEqual(stateA.overall_status_display, STATUS_LEGACY_WARNING_ZH, `overall_status_display === "${STATUS_LEGACY_WARNING_ZH}"`);
  assert(!stateA.overall_status_display.includes('正在运行'), 'Status does NOT say 正在运行');

  // Case 4B: NAMED_PIPE_LEGACY active transport
  const bridgeStatusLegacyB: Partial<BridgeConnectionState> = {
    isRunning: true,
    runtimeConnected: false,
    activeTransport: 'NAMED_PIPE_LEGACY',
  };

  const stateB = deriveRuntimeTruthState({
    selectedVersionId: 'xingshu-1.3.4',
    versions: mockVersions,
    installedRuntime: mockInstalledRuntime,
    gameProcess,
    bridgeStatus: bridgeStatusLegacyB as BridgeConnectionState,
  });

  assertStrictEqual(stateB.status_category, 'LEGACY_WARNING', 'Case 4B NAMED_PIPE_LEGACY -> LEGACY_WARNING');
  assertStrictEqual(stateB.legacy_detected, true, 'Case 4B legacy_detected === true');
  assertStrictEqual(stateB.overall_status_display, STATUS_LEGACY_WARNING_ZH, 'Case 4B displays legacy warning message');

  // Case 4C: Version contains RC1.1
  const bridgeStatusLegacyC: Partial<BridgeConnectionState> = {
    isRunning: true,
    runtimeConnected: false,
    runtimeVersion: 'RC1.1',
  };

  const stateC = deriveRuntimeTruthState({
    selectedVersionId: 'xingshu-1.3.4',
    versions: mockVersions,
    installedRuntime: mockInstalledRuntime,
    gameProcess,
    bridgeStatus: bridgeStatusLegacyC as BridgeConnectionState,
  });

  assertStrictEqual(stateC.status_category, 'LEGACY_WARNING', 'Case 4C RC1.1 runtimeVersion -> LEGACY_WARNING');
  assertStrictEqual(stateC.legacy_detected, true, 'Case 4C legacy_detected === true');
  assertStrictEqual(stateC.overall_status_display, STATUS_LEGACY_WARNING_ZH, 'Case 4C displays legacy warning message');

  // Check version badge under legacy condition
  const badgeLegacy = getVersionItemBadge(mockVersions[0], stateA, true);
  assertStrictEqual(badgeLegacy.label, '【旧版未接入】', 'Badge shows 【旧版未接入】');
  assertStrictEqual(badgeLegacy.isLiveActive, false, 'Badge isLiveActive is false');
});

// -----------------------------------------------------------------------------
// Test 5: Static Codebase Audit on RuntimePage.tsx
// -----------------------------------------------------------------------------
console.log('\n--- Test 5: Static Codebase Audit on RuntimePage.tsx ---');

runTest('Test 5: RuntimePage.tsx conforms to truth rules and consumes required APIs', () => {
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);
  const runtimePagePath = path.resolve(__dirname, '../pages/runtime/RuntimePage.tsx');

  assert(fs.existsSync(runtimePagePath), `RuntimePage.tsx exists at ${runtimePagePath}`);
  const content = fs.readFileSync(runtimePagePath, 'utf8');

  // 1. Must consume api.checkGameProcessStatus and api.getBridgeStatus
  assert(content.includes('api.checkGameProcessStatus'), 'Consumes api.checkGameProcessStatus');
  assert(content.includes('api.getBridgeStatus'), 'Consumes api.getBridgeStatus');

  // 2. Must import and use deriveRuntimeTruthState
  assert(content.includes('deriveRuntimeTruthState'), 'Imports and invokes deriveRuntimeTruthState');
  assert(content.includes('getVersionItemBadge'), 'Uses getVersionItemBadge');

  // 3. Must display the 4 required fields in the UI
  assert(content.includes('已选择方案'), 'Renders "已选择方案" (Selected Plan)');
  assert(content.includes('已安装版本'), 'Renders "已安装版本" (Installed Version)');
  assert(content.includes('当前游戏实际状态'), 'Renders "当前游戏实际状态" (Actual Game State)');
  assert(content.includes('Bridge 状态'), 'Renders "Bridge 状态" (Bridge State)');

  // 4. Must display prominent "运行时会话真实状态" card
  assert(content.includes('运行时会话真实状态'), 'Renders "运行时会话真实状态" card');
  assert(content.includes('CANONICAL_RUNTIME_HOST') || content.includes('XingShuRuntimeHost'), 'Mentions Canonical Host XingShuRuntimeHost');
  assert(content.includes('CANONICAL_TRANSPORT') || content.includes('Loopback TCP'), 'Mentions Active Transport Loopback TCP');
  assert(content.includes('handshake_status'), 'Displays handshake status');

  // 5. Must display legacy warning condition
  assert(content.includes('检测到旧版 Runtime，当前 XingShu Bridge 未连接'), 'Renders legacy warning text');

  // 6. Must NOT contain unconditional "{isZh ? '正在运行' : 'RUNNING ACTIVE'}" or similar false claim
  const forbiddenUnconditionalRunning = /ver\.is_active\s*&&\s*\(?<span[^>]*>[^<]*正在运行/;
  assert(
    !forbiddenUnconditionalRunning.test(content),
    'Zero instances of unconditional ver.is_active && 正在运行'
  );
});

// -----------------------------------------------------------------------------
// Summary
// -----------------------------------------------------------------------------
console.log('\n================================================================================');
console.log(`RUNTIME TRUTH TEST RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
console.log('================================================================================');

if (failCount > 0) {
  process.exit(1);
}
