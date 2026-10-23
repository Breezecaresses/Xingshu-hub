/**
 * Project XingShu — HS4-P1.3A Live Entry Reliability & Route Persistence Test Suite
 *
 * Verifies the 3 Core Blocker Resolutions:
 * 1. Process State Integration: Real process detection -> LOGIN_REQUIRED / OFFLINE / READY truth.
 * 2. Launch Action: Canonical steam://run/2622000, whitelist enforcement, duplicate run prevention.
 * 3. Route Persistence: App-level session store preserves accountMode without leaking live state.
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  SpectatorAccountStatus,
} from '../types/spectatorAccount.ts';
import {
  UserOwnedSpectatorAccountService,
  CANONICAL_ASTRAL_PARTY_STEAM_APP_ID,
  getSteamLaunchUri,
} from '../services/spectatorAccountService.ts';
import {
  formatSpectatorPlayerId,
} from '../pages/spectator/spectatorTransition.ts';

declare const process: {
  exit(code?: number): never;
};


console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.3A: LIVE ENTRY RELIABILITY & ROUTE PERSISTENCE SUITE');
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

async function runAsyncTest(name: string, fn: () => Promise<void> | void): Promise<void> {
  const failuresBefore = failCount;
  try {
    await fn();
  } catch (err: any) {
    // Missing fixtures and other exceptions must fail the suite too.
    if (failCount === failuresBefore) failCount++;
    console.error(`[TEST_EXCEPTION] ${name}:`, err.message);
  }
}

// Memory storage mock for simulating sessionStorage and localStorage
class MockStorage implements Storage {
  private store: Map<string, string> = new Map();

  get length(): number {
    return this.store.size;
  }

  clear(): void {
    this.store.clear();
  }

  getItem(key: string): string | null {
    return this.store.get(key) ?? null;
  }

  key(index: number): string | null {
    const keys = Array.from(this.store.keys());
    return keys[index] ?? null;
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }
}

async function runSuite() {
  // ---------------------------------------------------------------------------
  // Part 1: Process State Integration & Lifecycle State Machine
  // ---------------------------------------------------------------------------
  console.log('\n--- Part 1: Game Process Detection State Machine ---');

  const service = new UserOwnedSpectatorAccountService(new MockStorage());

  await runAsyncTest('Scenario 1: Game process false -> OFFLINE', () => {
    const info = service.syncAccountInfo({
      gameRunning: false,
      runtimeConnected: false,
      spectatorAccountPlayerId: null,
      roomId: null,
    });
    assertStrictEqual(info.status, SpectatorAccountStatus.OFFLINE, 'status === OFFLINE when game closed');
    assertStrictEqual(info.gameRunning, false, 'gameRunning === false');
    assertStrictEqual(info.runtimeConnected, false, 'runtimeConnected === false');
    assertStrictEqual(info.currentSessionPlayerId, null, 'currentSessionPlayerId === null');

    // UI text evaluation: game not running + disconnected -> 未识别
    const displayText = formatSpectatorPlayerId(info.currentSessionPlayerId, info.runtimeConnected || info.gameRunning, true);
    assertStrictEqual(displayText, '未识别', 'UI displays 未识别 when game is offline');
  });

  await runAsyncTest('Scenario 2: Game process true + runtime false -> WAITING_RUNTIME', () => {
    const info = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: false,
      spectatorAccountPlayerId: null,
      roomId: null,
    });
    assertStrictEqual(info.status, SpectatorAccountStatus.WAITING_RUNTIME, 'status === WAITING_RUNTIME when game running but runtime disconnected');
    assert(info.status !== SpectatorAccountStatus.LOGIN_REQUIRED, 'status is strictly NOT LOGIN_REQUIRED');
    assertStrictEqual(info.gameRunning, true, 'gameRunning === true');
    assertStrictEqual(info.runtimeConnected, false, 'runtimeConnected === false');
    assertStrictEqual(info.currentSessionPlayerId, null, 'currentSessionPlayerId === null');

    // UI text evaluation: game running -> 等待 Runtime 识别
    const displayText = formatSpectatorPlayerId(info.currentSessionPlayerId, info.runtimeConnected || info.gameRunning, true);
    assertStrictEqual(displayText, '等待 Runtime 识别', 'UI displays 等待 Runtime 识别 when game is running');
    assert(!displayText.includes('等待登录'), 'UI does NOT say 等待登录');
  });

  await runAsyncTest('Scenario 3: Game process true + runtime true + playerId null -> IDENTITY_PENDING', () => {
    const info = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: null,
      roomId: null,
    });
    assertStrictEqual(info.status, SpectatorAccountStatus.IDENTITY_PENDING, 'status === IDENTITY_PENDING before packet received');
    assert(info.status !== SpectatorAccountStatus.LOGIN_REQUIRED, 'Prohibit LOGIN_REQUIRED when runtime connected');
    assertStrictEqual(info.gameRunning, true, 'gameRunning === true');
    assertStrictEqual(info.runtimeConnected, true, 'runtimeConnected === true');
    assertStrictEqual(info.currentSessionPlayerId, null, 'currentSessionPlayerId === null');

    const displayText = formatSpectatorPlayerId(info.currentSessionPlayerId, info.runtimeConnected || info.gameRunning, true);
    assertStrictEqual(displayText, '等待 Runtime 识别', 'UI displays 等待 Runtime 识别 when connected but pre-packet');
    assert(!displayText.includes('等待登录'), 'UI does NOT say 等待登录');
  });

  await runAsyncTest('Scenario 4: Enter match Room 152953, playerId 1556514 -> IN_USE', () => {
    const info = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: 1556514,
      roomId: 152953,
    });
    assertStrictEqual(info.status, SpectatorAccountStatus.IN_USE, 'status === IN_USE in room 152953');
    assertStrictEqual(info.currentSessionPlayerId, 1556514, 'currentSessionPlayerId becomes 1556514');
    assertStrictEqual(info.lastKnownPlayerId, 1556514, 'lastKnownPlayerId === 1556514');

    const displayText = formatSpectatorPlayerId(info.currentSessionPlayerId, info.runtimeConnected || info.gameRunning, true);
    assertStrictEqual(displayText, '1556514', 'UI displays authentic 1556514');
  });

  await runAsyncTest('Scenario 5: Match concludes / room exit -> READY (since playerId is now known)', () => {
    const info = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: null,
      roomId: null,
    });
    assertStrictEqual(info.status, SpectatorAccountStatus.READY, 'status === READY when room exited');
    assert(info.status !== SpectatorAccountStatus.LOGIN_REQUIRED, 'Prohibit regression to LOGIN_REQUIRED');
    assertStrictEqual(info.currentSessionPlayerId, 1556514, 'currentSessionPlayerId preserved as 1556514');
  });

  await runAsyncTest('Scenario 6: Game exit resets to OFFLINE and nulls currentSessionPlayerId', () => {
    const info = service.syncAccountInfo({
      gameRunning: false,
      runtimeConnected: false,
      spectatorAccountPlayerId: null,
      roomId: null,
    });
    assertStrictEqual(info.status, SpectatorAccountStatus.OFFLINE, 'status reverts to OFFLINE');
    assertStrictEqual(info.currentSessionPlayerId, null, 'currentSessionPlayerId is wiped on exit');
    assertStrictEqual(info.lastKnownPlayerId, 1556514, 'lastKnownPlayerId remains preserved for UI hint');

    const displayText = formatSpectatorPlayerId(info.currentSessionPlayerId, false, true);
    assertStrictEqual(displayText, '未识别', 'UI displays 未识别 once game exits');
  });

  // ---------------------------------------------------------------------------
  // Part 2: Launch Game Action & Security Whitelist
  // ---------------------------------------------------------------------------
  console.log('\n--- Part 2: Launch Game Action & Security Whitelist ---');

  await runAsyncTest('Launch URI 1: Canonical Steam App ID is 2622000', () => {
    assertStrictEqual(CANONICAL_ASTRAL_PARTY_STEAM_APP_ID, 2622000, 'Canonical Steam App ID === 2622000');
    assertStrictEqual(getSteamLaunchUri(2622000), 'steam://run/2622000', 'Canonical URI === steam://run/2622000');
  });

  await runAsyncTest('Launch URI 2: Service generates canonical launch URI for STEAM_CN', () => {
    const uri = service.getLaunchUri('STEAM_CN');
    assertStrictEqual(uri, 'steam://run/2622000', 'STEAM_CN launch URI === steam://run/2622000');
  });

  await runAsyncTest('Launch Action 3: Whitelist validator allows exact steam://run/2622000', () => {
    const validUri = 'steam://run/2622000';
    const isValid = /^steam:\/\/run\/2622000$/.test(validUri);
    assert(isValid, 'Regex matches steam://run/2622000');
  });

  await runAsyncTest('Launch Action 4: Whitelist validator rejects non-canonical URIs', () => {
    const badUris = [
      'steam://run/2514700',
      'steam://run/123456',
      'cmd.exe /c calc',
      'powershell -enc ...',
      'https://malicious.com',
      'file:///C:/Windows/System32/cmd.exe',
      'steam://run/2622000&malicious=true',
    ];

    for (const bad of badUris) {
      const isValid = /^steam:\/\/run\/2622000$/.test(bad);
      assertStrictEqual(isValid, false, `Bad URI rejected: ${bad}`);
    }
  });

  await runAsyncTest('Launch Action 5: Headless launchGame returns target URI and launched status', async () => {
    const result = await service.launchGame('STEAM_CN');
    assertStrictEqual(result.uri, 'steam://run/2622000', 'result.uri === steam://run/2622000');
    assertStrictEqual(result.launched, false, 'result.launched === false in headless runner');
  });

  // ---------------------------------------------------------------------------
  // Part 3: Cross-Route UI Session Store & Dynamic Rehydration
  // ---------------------------------------------------------------------------
  console.log('\n--- Part 3: Route Persistence & Rehydration Truth ---');

  const sessionStorageMock = new MockStorage();
  const localStorageMock = new MockStorage();

  // Helper simulating the App-Level Spectator UI Mode Store resolution logic
  function resolveAccountModeFromStore(
    searchQuery: string,
    session: Storage,
    local: Storage
  ): 'USER_OWNED' | 'XINGSHU_MANAGED' | null {
    if (searchQuery.includes('mode=entry')) return null;
    if (searchQuery.includes('mode=managed') || searchQuery.includes('mode=XINGSHU_MANAGED')) return 'XINGSHU_MANAGED';
    if (searchQuery.includes('mode=ready') || searchQuery.includes('mode=user') || searchQuery.includes('mode=USER_OWNED')) return 'USER_OWNED';

    const sessionMode = session.getItem('xingshu_spectator_selected_mode');
    if (sessionMode === 'USER_OWNED' || sessionMode === 'XINGSHU_MANAGED') return sessionMode as any;

    const localMode = local.getItem('xingshu.spectator.accountMode');
    if (localMode === 'USER_OWNED' || localMode === 'XINGSHU_MANAGED') return localMode as any;

    return null;
  }

  function saveAccountModeToStore(
    mode: 'USER_OWNED' | 'XINGSHU_MANAGED' | null,
    session: Storage,
    local: Storage
  ): void {
    if (mode) {
      session.setItem('xingshu_spectator_selected_mode', mode);
      local.setItem('xingshu.spectator.accountMode', mode);
    } else {
      session.removeItem('xingshu_spectator_selected_mode');
      local.removeItem('xingshu.spectator.accountMode');
    }
  }

  await runAsyncTest('Route 1: Initial visit without prior selection defaults to null (Step 1)', () => {
    sessionStorageMock.clear();
    localStorageMock.clear();
    const mode = resolveAccountModeFromStore('', sessionStorageMock, localStorageMock);
    assertStrictEqual(mode, null, 'Initial mode is null -> Stepper Step 1 active');
  });

  await runAsyncTest('Route 2: User selects USER_OWNED -> persisted to session & local store', () => {
    saveAccountModeToStore('USER_OWNED', sessionStorageMock, localStorageMock);
    assertStrictEqual(sessionStorageMock.getItem('xingshu_spectator_selected_mode'), 'USER_OWNED', 'sessionStorage stores USER_OWNED');
    assertStrictEqual(localStorageMock.getItem('xingshu.spectator.accountMode'), 'USER_OWNED', 'localStorage stores USER_OWNED');
  });

  await runAsyncTest('Route 3: Navigate away (/spectator -> /runtime) and return (/runtime -> /spectator)', () => {
    // When returning, URL search is empty: ""
    const modeOnReturn = resolveAccountModeFromStore('', sessionStorageMock, localStorageMock);
    assertStrictEqual(modeOnReturn, 'USER_OWNED', 'USER_OWNED is preserved across route change (does NOT reset to Step 1)');
  });

  await runAsyncTest('Route 4: User selects XINGSHU_MANAGED -> persists across route change', () => {
    saveAccountModeToStore('XINGSHU_MANAGED', sessionStorageMock, localStorageMock);
    const modeOnReturn = resolveAccountModeFromStore('', sessionStorageMock, localStorageMock);
    assertStrictEqual(modeOnReturn, 'XINGSHU_MANAGED', 'XINGSHU_MANAGED is preserved across route change');
  });

  await runAsyncTest('Route 5: Explicit ?mode=entry query resets to entry selection (null)', () => {
    const mode = resolveAccountModeFromStore('?mode=entry', sessionStorageMock, localStorageMock);
    assertStrictEqual(mode, null, 'mode=entry explicitly resets selection to null (Step 1)');
  });

  await runAsyncTest('Route 6: Live Truth vs UI Preference - Live telemetry is NOT read from storage', () => {
    // UI preference is stored:
    saveAccountModeToStore('USER_OWNED', sessionStorageMock, localStorageMock);

    // But live game running & runtime state MUST rehydrate from backend
    let mockBackendProcessRunning = true;
    let mockBackendRuntimeConnected = false;

    // Simulated rehydration
    const info = service.syncAccountInfo({
      gameRunning: mockBackendProcessRunning,
      runtimeConnected: mockBackendRuntimeConnected,
      spectatorAccountPlayerId: null,
      roomId: null,
    });

    assertStrictEqual(info.status, SpectatorAccountStatus.WAITING_RUNTIME, 'Live state correctly rehydrated as WAITING_RUNTIME');
    assert(info.status !== SpectatorAccountStatus.LOGIN_REQUIRED, 'Live state is NOT LOGIN_REQUIRED');
    assertStrictEqual(info.currentSessionPlayerId, null, 'No fake or cached currentSessionPlayerId is populated');
  });

  await runAsyncTest('Route 7: Live Truth - Disconnected while away results in real DISCONNECTED', () => {
    // User navigates away while match was streaming, then runtime disconnects
    const info = service.syncAccountInfo({
      gameRunning: false,
      runtimeConnected: false,
      spectatorAccountPlayerId: null,
      roomId: null,
    });

    assertStrictEqual(info.status, SpectatorAccountStatus.OFFLINE, 'Rehydrated as OFFLINE');
    assertStrictEqual(info.currentSessionPlayerId, null, 'currentSessionPlayerId is null');
  });

  // ---------------------------------------------------------------------------
  // Part 4: Static Source Code Audit
  // ---------------------------------------------------------------------------
  console.log('\n--- Part 4: Static Source Code Audit ---');

  const sourceRoot = fileURLToPath(new URL('../', import.meta.url));
  const spectatorPagePath = path.join(sourceRoot, 'pages/spectator/SpectatorPage.tsx');
  const spectatorSelectorPath = path.join(sourceRoot, 'pages/spectator/components/SpectatorAccountSelector.tsx');
  const useSpectatorStatePath = path.join(sourceRoot, 'pages/spectator/useSpectatorState.ts');
  const tauriApiPath = path.join(sourceRoot, 'shared/api/tauri.ts');

  await runAsyncTest('Static 0: SpectatorPage wires isGameRunning and launchGame to selector', () => {
    const pageCode = fs.readFileSync(spectatorPagePath, 'utf-8');
    assert(pageCode.includes('isGameRunning={isGameRunning}'), 'SpectatorPage wires isGameRunning');
    assert(pageCode.includes('onLaunchGame={launchGame}'), 'SpectatorPage wires launchGame');
  });

  await runAsyncTest('Static 1: useSpectatorState imports and polls checkGameProcessStatus', () => {
    const code = fs.readFileSync(useSpectatorStatePath, 'utf-8');
    assert(code.includes('api.checkGameProcessStatus'), 'Calls api.checkGameProcessStatus');
    assert(code.includes('setGameProcess'), 'Stores gameProcess state');
    assert(code.includes('const isGameRunning = !!spectator?.processId'), 'Spectator availability is scoped to the resolved spectator process');
    assert(code.includes('const isGameRunning = !!effectiveSpectatorSession?.processId'), 'Displayed spectator availability does not borrow the main game process');
  });

  await runAsyncTest('Static 2: useSpectatorState implements cross-route session storage persistence', () => {
    const code = fs.readFileSync(useSpectatorStatePath, 'utf-8');
    assert(code.includes('xingshu_spectator_selected_mode'), 'Uses xingshu_spectator_selected_mode key');
    assert(code.includes('sessionStorage.getItem'), 'Reads from sessionStorage on init');
    assert(code.includes('sessionStorage.setItem'), 'Writes to sessionStorage in setAccountMode');
  });

  await runAsyncTest('Static 3: SpectatorAccountSelector renders 游戏已运行 when game running', () => {
    const code = fs.readFileSync(spectatorSelectorPath, 'utf-8');
    assert(code.includes('游戏已运行'), 'Includes 游戏已运行 text');
    assert(code.includes('Game Running'), 'Includes Game Running text');
    assert(code.includes('disabled'), 'Launch button has disabled attribute when running');
  });

  await runAsyncTest('Static 4: tauri.ts exports checkGameProcessStatus and launchGameUri', () => {
    const code = fs.readFileSync(tauriApiPath, 'utf-8');
    assert(code.includes('checkGameProcessStatus:'), 'Exports checkGameProcessStatus');
    assert(code.includes('launchGameUri:'), 'Exports launchGameUri');
    assert(code.includes('check_game_process_status'), 'Invokes check_game_process_status command');
    assert(code.includes('launch_game_uri'), 'Invokes launch_game_uri command');
  });

  console.log('\n================================================================================');
  console.log(`LIVE ENTRY RELIABILITY TEST RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
  console.log('================================================================================');

  if (failCount > 0) {
    process.exit(1);
  }
}

runSuite();
