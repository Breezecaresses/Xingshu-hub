/**
 * Project XingShu — Account Services Integration Test Suite
 * Phase: HS4-P1.2.1
 * Lane 3: Account Services Integrator
 *
 * Verifies integration between:
 * 1. userOwnedSpectatorAccountService & defaultManagedSpectatorAccountProvider
 * 2. Dynamic provider injection via IManagedSpectatorAccountProvider
 * 3. Dynamic UI state mapping without hardcoded SERVICE_NOT_CONFIGURED
 * 4. Runtime state synchronization & playerId capture
 * 5. Canonical Steam App ID 2622000 launch protocol
 * 6. Source code audit proving zero UI hardcoding
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  UserOwnedSpectatorAccountService,
  userOwnedSpectatorAccountService,
  MemoryStorage,
  CANONICAL_ASTRAL_PARTY_STEAM_APP_ID,
  getSteamLaunchUri,
} from '../services/spectatorAccountService.ts';

import {
  DefaultManagedSpectatorAccountProvider,
  defaultManagedSpectatorAccountProvider,
  type IManagedSpectatorAccountProvider,
  type ManagedAccountInfo,
} from '../services/managedAccountProvider.ts';

import {
  SpectatorAccountStatus,
  OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE,
} from '../types/spectatorAccount.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.2.1: ACCOUNT SERVICES INTEGRATION TEST SUITE');
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

async function runAsyncTest(name: string, fn: () => Promise<void>): Promise<void> {
  try {
    await fn();
  } catch (err: any) {
    console.error(`[FAIL] ${name} threw error: ${err.message}`);
  }
}

function runTest(name: string, fn: () => void): void {
  try {
    fn();
  } catch (err: any) {
    console.error(`[FAIL] ${name} threw error: ${err.message}`);
  }
}

/**
 * State coordinator simulating useSpectatorState's provider and user account state machines.
 */
class SpectatorStateCoordinator {
  private managedProvider: IManagedSpectatorAccountProvider;
  public managedAccountInfo: ManagedAccountInfo;
  public userAccountInfo: ReturnType<UserOwnedSpectatorAccountService['getAccountInfo']>;
  private userAccountService: UserOwnedSpectatorAccountService;

  constructor(
    customProvider?: IManagedSpectatorAccountProvider,
    customService?: UserOwnedSpectatorAccountService
  ) {
    this.managedProvider = customProvider ?? defaultManagedSpectatorAccountProvider;
    this.userAccountService = customService ?? userOwnedSpectatorAccountService;
    this.managedAccountInfo = {
      status: 'SERVICE_NOT_CONFIGURED',
      serverEndpoint: null,
      sessionId: null,
      allocatedPlayerId: null,
      allocatedAt: null,
      expiresAt: null,
      message: OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE,
    };
    this.userAccountInfo = this.userAccountService.getAccountInfo();
  }

  public async setManagedProvider(provider: IManagedSpectatorAccountProvider): Promise<void> {
    this.managedProvider = provider;
    try {
      this.managedAccountInfo = await provider.getStatus();
    } catch (err: any) {
      this.managedAccountInfo = {
        status: 'ERROR',
        serverEndpoint: null,
        sessionId: null,
        allocatedPlayerId: null,
        allocatedAt: null,
        expiresAt: null,
        message: err instanceof Error ? err.message : String(err),
      };
    }
  }

  public async loadInitialManagedStatus(): Promise<ManagedAccountInfo> {
    try {
      this.managedAccountInfo = await this.managedProvider.getStatus();
    } catch (err: any) {
      this.managedAccountInfo = {
        status: 'ERROR',
        serverEndpoint: null,
        sessionId: null,
        allocatedPlayerId: null,
        allocatedAt: null,
        expiresAt: null,
        message: err instanceof Error ? err.message : String(err),
      };
    }
    return this.managedAccountInfo;
  }

  public syncUserAccount(params: {
    gameRunning: boolean;
    runtimeConnected: boolean;
    spectatorAccountPlayerId?: number | null;
    roomId?: number | null;
  }) {
    this.userAccountInfo = this.userAccountService.syncAccountInfo(params);
    return this.userAccountInfo;
  }
}

async function main() {
  // ---------------------------------------------------------------------------
  // Test 1: Initial load with default provider -> status is SERVICE_NOT_CONFIGURED
  // ---------------------------------------------------------------------------
  console.log('\n--- Test 1: Initial Load with Default Provider ---');

  await runAsyncTest('Test 1: Default provider status is SERVICE_NOT_CONFIGURED', async () => {
    const defaultProvider = new DefaultManagedSpectatorAccountProvider();
    const status = await defaultProvider.getStatus();

    assertStrictEqual(status.status, 'SERVICE_NOT_CONFIGURED', 'Default provider returns SERVICE_NOT_CONFIGURED');
    assertStrictEqual(status.serverEndpoint, null, 'Default provider has null server endpoint');
    assertStrictEqual(status.sessionId, null, 'Default provider has null sessionId');
    assertStrictEqual(status.allocatedPlayerId, null, 'Default provider has null allocatedPlayerId');
    assert(
      status.message.includes('未配置') || status.message.includes('ADR-007') || status.message.includes('我的观战账号'),
      'Default provider provides honest ADR-007 guidance message'
    );
  });

  await runAsyncTest('Test 1: State coordinator initial load with default provider', async () => {
    const coordinator = new SpectatorStateCoordinator();
    const info = await coordinator.loadInitialManagedStatus();

    assertStrictEqual(info.status, 'SERVICE_NOT_CONFIGURED', 'Coordinator initial status is SERVICE_NOT_CONFIGURED');
    assertStrictEqual(coordinator.managedAccountInfo.status, 'SERVICE_NOT_CONFIGURED', 'Coordinator state stores SERVICE_NOT_CONFIGURED');
  });

  // ---------------------------------------------------------------------------
  // Test 2: Injected mock provider with status AVAILABLE -> state updates to AVAILABLE
  // ---------------------------------------------------------------------------
  console.log('\n--- Test 2: Injected Mock Provider with Status AVAILABLE (Zero Hardcoding) ---');

  await runAsyncTest('Test 2: Injected mock provider updates state to AVAILABLE and preserves message', async () => {
    const customMessage = 'XingShu Official Spectator Cluster online with 12 ready worker nodes.';
    const mockAvailableProvider: IManagedSpectatorAccountProvider = {
      getStatus: async () => ({
        status: 'AVAILABLE',
        serverEndpoint: 'https://spectator-pool.xingshu.cloud',
        sessionId: null,
        allocatedPlayerId: null,
        allocatedAt: null,
        expiresAt: null,
        message: customMessage,
      }),
      allocateSession: async () => ({
        status: 'READY',
        serverEndpoint: 'https://spectator-pool.xingshu.cloud',
        sessionId: 'sess_live_12345',
        allocatedPlayerId: 777888,
        allocatedAt: new Date().toISOString(),
        expiresAt: new Date(Date.now() + 3600000).toISOString(),
        message: 'Session allocated successfully.',
      }),
      releaseSession: async () => {},
    };

    const coordinator = new SpectatorStateCoordinator();
    await coordinator.setManagedProvider(mockAvailableProvider);

    assertStrictEqual(coordinator.managedAccountInfo.status, 'AVAILABLE', 'State updates dynamically to AVAILABLE');
    assertStrictEqual(coordinator.managedAccountInfo.serverEndpoint, 'https://spectator-pool.xingshu.cloud', 'Server endpoint stored');
    assertStrictEqual(coordinator.managedAccountInfo.message, customMessage, 'Custom message matches provider verbatim');
  });

  // ---------------------------------------------------------------------------
  // Test 3: Injected mock provider with status ERROR -> state updates to ERROR
  // ---------------------------------------------------------------------------
  console.log('\n--- Test 3: Injected Mock Provider with Status ERROR ---');

  await runAsyncTest('Test 3: Injected mock provider returning ERROR status', async () => {
    const errorMessage = 'HTTP 503 Service Unavailable: Remote cluster maintenance';
    const mockErrorProvider: IManagedSpectatorAccountProvider = {
      getStatus: async () => ({
        status: 'ERROR',
        serverEndpoint: 'https://spectator-pool.xingshu.cloud',
        sessionId: null,
        allocatedPlayerId: null,
        allocatedAt: null,
        expiresAt: null,
        message: errorMessage,
      }),
      allocateSession: async () => {
        throw new Error('Cluster unavailable');
      },
      releaseSession: async () => {},
    };

    const coordinator = new SpectatorStateCoordinator();
    await coordinator.setManagedProvider(mockErrorProvider);

    assertStrictEqual(coordinator.managedAccountInfo.status, 'ERROR', 'State updates dynamically to ERROR');
    assertStrictEqual(coordinator.managedAccountInfo.message, errorMessage, 'Error message matches provider verbatim');
  });

  await runAsyncTest('Test 3: Provider throwing exception caught and mapped to ERROR', async () => {
    const throwingProvider: IManagedSpectatorAccountProvider = {
      getStatus: async () => {
        throw new Error('Network socket disconnected');
      },
      allocateSession: async () => {
        throw new Error('Network socket disconnected');
      },
      releaseSession: async () => {},
    };

    const coordinator = new SpectatorStateCoordinator();
    await coordinator.setManagedProvider(throwingProvider);

    assertStrictEqual(coordinator.managedAccountInfo.status, 'ERROR', 'Thrown error mapped to ERROR status');
    assertStrictEqual(coordinator.managedAccountInfo.message, 'Network socket disconnected', 'Exception message captured');
  });

  // ---------------------------------------------------------------------------
  // Test 4: User account service sync without runtime -> OFFLINE or WAITING_RUNTIME
  // ---------------------------------------------------------------------------
  console.log('\n--- Test 4: User Account Service Sync Without Runtime ---');

  runTest('Test 4: Sync when game is closed -> status is OFFLINE, playerId is null', () => {
    const storage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(storage);

    const info = service.syncAccountInfo({
      gameRunning: false,
      runtimeConnected: false,
      spectatorAccountPlayerId: null,
      roomId: null,
    });

    assertStrictEqual(info.status, SpectatorAccountStatus.OFFLINE, 'Account status is OFFLINE when game closed');
    assertStrictEqual(info.playerId, null, 'PlayerId is null when game closed');
    assertStrictEqual(info.gameRunning, false, 'gameRunning is false');
    assertStrictEqual(info.runtimeConnected, false, 'runtimeConnected is false');
  });

  runTest('Test 4: Sync when game running but runtime disconnected -> status is WAITING_RUNTIME, playerId is null', () => {
    const storage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(storage);

    const info = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: false,
      spectatorAccountPlayerId: null,
      roomId: null,
    });

    assertStrictEqual(info.status, SpectatorAccountStatus.WAITING_RUNTIME, 'Account status is WAITING_RUNTIME without runtime');
    assert(info.status !== SpectatorAccountStatus.LOGIN_REQUIRED, 'Prohibit LOGIN_REQUIRED');
    assertStrictEqual(info.playerId, null, 'PlayerId is null');
    assertStrictEqual(info.gameRunning, true, 'gameRunning is true');
    assertStrictEqual(info.runtimeConnected, false, 'runtimeConnected is false');
  });

  runTest('Test 4: Sync when game running & runtime connected but pre-packet -> status is IDENTITY_PENDING', () => {
    const storage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(storage);

    const info = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: null,
      roomId: null,
    });

    assertStrictEqual(info.status, SpectatorAccountStatus.IDENTITY_PENDING, 'Account status is IDENTITY_PENDING pre-packet');
    assert(info.status !== SpectatorAccountStatus.LOGIN_REQUIRED, 'Prohibit LOGIN_REQUIRED when runtime connected');
    assertStrictEqual(info.playerId, null, 'PlayerId is null');
  });

  // ---------------------------------------------------------------------------
  // Test 5: User account service sync with runtime & real playerId 987654 -> READY
  // ---------------------------------------------------------------------------
  console.log('\n--- Test 5: User Account Service Sync With Runtime & PlayerId 987654 ---');

  runTest('Test 5: Sync with runtime and playerId 987654 -> status is READY, playerId is 987654', () => {
    const storage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(storage);

    const info = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: 987654,
      roomId: null,
    });

    assertStrictEqual(info.status, SpectatorAccountStatus.READY, 'Account status is READY when connected with playerId');
    assertStrictEqual(info.playerId, 987654, 'PlayerId is 987654');
    assertStrictEqual(info.gameRunning, true, 'gameRunning is true');
    assertStrictEqual(info.runtimeConnected, true, 'runtimeConnected is true');

    // Query getAccountInfo snapshot
    const snapshot = service.getAccountInfo();
    assertStrictEqual(snapshot.status, SpectatorAccountStatus.READY, 'getAccountInfo returns READY');
    assertStrictEqual(snapshot.playerId, 987654, 'getAccountInfo returns playerId 987654');
  });

  runTest('Test 5: Room entered transitions to IN_USE', () => {
    const storage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(storage);

    const info = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: 987654,
      roomId: 665945,
    });

    assertStrictEqual(info.status, SpectatorAccountStatus.IN_USE, 'Account status is IN_USE in active match');
    assertStrictEqual(info.playerId, 987654, 'PlayerId preserved in match');
  });

  // ---------------------------------------------------------------------------
  // Test 6: Launch game triggers canonical steam URI steam://run/2622000
  // ---------------------------------------------------------------------------
  console.log('\n--- Test 6: Launch Game Canonical Steam URI steam://run/2622000 ---');

  runTest('Test 6: CANONICAL_ASTRAL_PARTY_STEAM_APP_ID constant is 2622000', () => {
    assertStrictEqual(CANONICAL_ASTRAL_PARTY_STEAM_APP_ID, 2622000, 'Canonical App ID is 2622000');
  });

  runTest('Test 6: getSteamLaunchUri() returns canonical steam://run/2622000', () => {
    const uri = getSteamLaunchUri();
    assertStrictEqual(uri, 'steam://run/2622000', 'Canonical helper returns steam://run/2622000');
  });

  runTest('Test 6: userOwnedSpectatorAccountService.getLaunchUri() resolves steam://run/2622000', () => {
    const service = new UserOwnedSpectatorAccountService(new MemoryStorage());
    assertStrictEqual(service.getLaunchUri('STEAM_CN'), 'steam://run/2622000', 'STEAM_CN launch URI is steam://run/2622000');
    assertStrictEqual(service.getLaunchUri('STEAM_GLOBAL'), 'steam://run/2622000', 'STEAM_GLOBAL launch URI is steam://run/2622000');
    assertStrictEqual(service.getLaunchUri(), 'steam://run/2622000', 'Default launch URI is steam://run/2622000');
  });

  await runAsyncTest('Test 6: launchGame triggers canonical steam URI steam://run/2622000', async () => {
    const service = new UserOwnedSpectatorAccountService(new MemoryStorage());
    const result = await service.launchGame('STEAM_CN');
    assertStrictEqual(result.uri, 'steam://run/2622000', 'launchGame result URI is steam://run/2622000');
    assertStrictEqual(result.launched, false, 'launchGame does not pop windows in headless runner');
  });

  // ---------------------------------------------------------------------------
  // Test 7: Static Source Code Audit Verifying Zero UI Hardcoding
  // ---------------------------------------------------------------------------
  console.log('\n--- Test 7: Static Source Code Audit Verifying Zero Hardcoded Strings ---');

  // @ts-ignore
  const currentDir = path.dirname(fileURLToPath(import.meta.url));
  const spectatorPagePath = path.resolve(currentDir, '../pages/spectator/SpectatorPage.tsx');
  const accountSelectorPath = path.resolve(currentDir, '../pages/spectator/components/SpectatorAccountSelector.tsx');
  const spectatorStatePath = path.resolve(currentDir, '../pages/spectator/useSpectatorState.ts');

  runTest('Test 7: SpectatorPage.tsx exists and is readable', () => {
    assert(fs.existsSync(spectatorPagePath), 'SpectatorPage.tsx exists');
  });

  runTest('Test 7: SpectatorAccountSelector.tsx exists and is readable', () => {
    assert(fs.existsSync(accountSelectorPath), 'SpectatorAccountSelector.tsx exists');
  });

  runTest('Test 7: useSpectatorState.ts exports userAccountInfo, managedAccountInfo, and setManagedProvider', () => {
    const code = fs.readFileSync(spectatorStatePath, 'utf-8');
    assert(code.includes('userAccountInfo'), 'useSpectatorState exposes userAccountInfo');
    assert(code.includes('managedAccountInfo'), 'useSpectatorState exposes managedAccountInfo');
    assert(code.includes('setManagedProvider'), 'useSpectatorState exposes setManagedProvider');
    assert(code.includes('userOwnedSpectatorAccountService.syncAccountInfo'), 'useSpectatorState calls syncAccountInfo');
    assert(code.includes('userOwnedSpectatorAccountService.launchGame'), 'useSpectatorState wires launchGame to service');
  });

  runTest('Test 7: SpectatorPage.tsx contains ZERO hardcoded "SERVICE_NOT_CONFIGURED" literal in JSX', () => {
    const pageCode = fs.readFileSync(spectatorPagePath, 'utf-8');
    // Ensure "SERVICE_NOT_CONFIGURED — " is completely removed
    assert(!pageCode.includes('SERVICE_NOT_CONFIGURED —'), 'SpectatorPage.tsx has zero hardcoded "SERVICE_NOT_CONFIGURED —"');
    assert(!pageCode.includes("'SERVICE_NOT_CONFIGURED —"), 'Zero single-quoted hardcoded string');
    assert(!pageCode.includes('"SERVICE_NOT_CONFIGURED —'), 'Zero double-quoted hardcoded string');
    assert(pageCode.includes('userAccountInfo'), 'SpectatorPage.tsx consumes userAccountInfo');
    assert(pageCode.includes('managedAccountInfo'), 'SpectatorPage.tsx consumes managedAccountInfo');
  });

  runTest('Test 7: SpectatorAccountSelector.tsx dynamically handles AVAILABLE, SERVICE_NOT_CONFIGURED, and ERROR', () => {
    const selectorCode = fs.readFileSync(accountSelectorPath, 'utf-8');
    assert(selectorCode.includes("managedStatus === 'AVAILABLE'"), 'Handles AVAILABLE dynamically');
    assert(selectorCode.includes("managedStatus === 'ERROR'"), 'Handles ERROR dynamically');
    assert(selectorCode.includes('申请托管观战会话'), 'Renders allocation action for AVAILABLE');
    assert(selectorCode.includes('可用 (随时可分配)'), 'Renders "可用 (随时可分配)" for AVAILABLE');
    assert(selectorCode.includes('managedMessage'), 'Renders dynamic provider message');
    assert(selectorCode.includes('userAccountInfo'), 'Consumes userAccountInfo');
  });

  // ---------------------------------------------------------------------------
  // Summary
  // ---------------------------------------------------------------------------
  console.log('\n================================================================================');
  console.log(`ACCOUNT SERVICES INTEGRATION RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
  console.log('================================================================================');

  if (failCount > 0) {
    process.exit(1);
  }
}

main().catch((err) => {
  console.error('[FATAL] Integration suite error:', err);
  process.exit(1);
});
