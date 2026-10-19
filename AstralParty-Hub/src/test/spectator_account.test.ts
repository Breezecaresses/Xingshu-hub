/**
 * Project XingShu — HS4-P1.2 User-Owned Spectator Account Test Suite
 * Conformance, Lifecycle State Derivation, Zero-Credential Security Guarantee,
 * and Launcher Protocol Assertions.
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  SpectatorAccountSource,
  SpectatorAccountStatus,
} from '../types/spectatorAccount.ts';

import {
  UserOwnedSpectatorAccountService,
  MemoryStorage,
  USER_SPECTATOR_ACCOUNT_STORAGE_KEY,
  FORBIDDEN_SECURITY_KEYS,
  userOwnedSpectatorAccountService,
} from '../services/spectatorAccountService.ts';
import { formatSpectatorPlayerId } from '../pages/spectator/spectatorTransition.ts';

declare const process: {
  exit(code?: number): never;
  env: Record<string, string | undefined>;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.2: USER-OWNED SPECTATOR ACCOUNT TEST SUITE');
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

async function runAsyncTest(name: string, fn: () => Promise<void>): Promise<void> {
  try {
    await fn();
  } catch (err: any) {
    console.error(`[TEST_EXCEPTION] ${name}:`, err.message);
  }
}

async function main() {
  // ---------------------------------------------------------------------------
  // Test Group 1: Enum and Type Conformance
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 1: Enum and Type Conformance ---');

  runTest('Group 1: SpectatorAccountSource values conform to schema', () => {
    assertStrictEqual(SpectatorAccountSource.USER_OWNED, 'USER_OWNED', 'SpectatorAccountSource.USER_OWNED');
    assertStrictEqual(SpectatorAccountSource.XINGSHU_MANAGED, 'XINGSHU_MANAGED', 'SpectatorAccountSource.XINGSHU_MANAGED');
  });

  runTest('Group 1: SpectatorAccountStatus contains all 10 required lifecycle states', () => {
    assertStrictEqual(SpectatorAccountStatus.UNCONFIGURED, 'UNCONFIGURED', 'SpectatorAccountStatus.UNCONFIGURED');
    assertStrictEqual(SpectatorAccountStatus.AVAILABLE, 'AVAILABLE', 'SpectatorAccountStatus.AVAILABLE');
    assertStrictEqual(SpectatorAccountStatus.LOGIN_REQUIRED, 'LOGIN_REQUIRED', 'SpectatorAccountStatus.LOGIN_REQUIRED');
    assertStrictEqual(SpectatorAccountStatus.WAITING_RUNTIME, 'WAITING_RUNTIME', 'SpectatorAccountStatus.WAITING_RUNTIME');
    assertStrictEqual(SpectatorAccountStatus.IDENTITY_PENDING, 'IDENTITY_PENDING', 'SpectatorAccountStatus.IDENTITY_PENDING');
    assertStrictEqual(SpectatorAccountStatus.ALLOCATING, 'ALLOCATING', 'SpectatorAccountStatus.ALLOCATING');
    assertStrictEqual(SpectatorAccountStatus.READY, 'READY', 'SpectatorAccountStatus.READY');
    assertStrictEqual(SpectatorAccountStatus.IN_USE, 'IN_USE', 'SpectatorAccountStatus.IN_USE');
    assertStrictEqual(SpectatorAccountStatus.OFFLINE, 'OFFLINE', 'SpectatorAccountStatus.OFFLINE');
    assertStrictEqual(SpectatorAccountStatus.ERROR, 'ERROR', 'SpectatorAccountStatus.ERROR');
  });

  // ---------------------------------------------------------------------------
  // Test Group 2: Operational Status Derivation
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 2: State Derivation & Transitions ---');

  runTest('Group 2: Game not running derives OFFLINE', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    const status1 = service.deriveStatus({
      gameRunning: false,
      runtimeConnected: false,
    });
    assertStrictEqual(status1, SpectatorAccountStatus.OFFLINE, 'gameRunning=false, runtimeConnected=false -> OFFLINE');

    const status2 = service.deriveStatus({
      gameRunning: false,
      runtimeConnected: true,
      spectatorAccountPlayerId: 1556514,
    });
    assertStrictEqual(status2, SpectatorAccountStatus.OFFLINE, 'gameRunning=false overrides connected runtime -> OFFLINE');
  });

  runTest('Group 2: Game running but runtime not connected derives WAITING_RUNTIME', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    const status = service.deriveStatus({
      gameRunning: true,
      runtimeConnected: false,
    });
    assertStrictEqual(status, SpectatorAccountStatus.WAITING_RUNTIME, 'gameRunning=true, runtimeConnected=false -> WAITING_RUNTIME');
    assert(status !== SpectatorAccountStatus.LOGIN_REQUIRED, 'status is strictly NOT LOGIN_REQUIRED');
  });

  runTest('Group 2: Game running, runtime connected, but playerId not received derives IDENTITY_PENDING', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    const status = service.deriveStatus({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: null,
      roomId: null,
    });
    assertStrictEqual(status, SpectatorAccountStatus.IDENTITY_PENDING, 'runtimeConnected=true, playerId=null -> IDENTITY_PENDING');
    assert(status !== SpectatorAccountStatus.LOGIN_REQUIRED, 'status is strictly NOT LOGIN_REQUIRED');
  });

  runTest('Group 2: Runtime connected & spectatorAccountPlayerId received derives READY', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    const status = service.deriveStatus({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: 1556514,
      roomId: null,
    });
    assertStrictEqual(status, SpectatorAccountStatus.READY, 'runtimeConnected=true, playerId=1556514 -> READY');
  });

  runTest('Group 2: Active match with roomId derives IN_USE (even before or with playerId)', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    const status1 = service.deriveStatus({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: null,
      roomId: 152953,
    });
    assertStrictEqual(status1, SpectatorAccountStatus.IN_USE, 'active roomId=152953 (playerId null) -> IN_USE');

    const status2 = service.deriveStatus({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: 1556514,
      roomId: 152953,
    });
    assertStrictEqual(status2, SpectatorAccountStatus.IN_USE, 'active roomId=152953 (playerId known) -> IN_USE');
  });

  runTest('Group 2: Authentic user live path (Offline -> Waiting Runtime -> Identity Pending -> In Use -> Ready -> Offline)', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    // 1. Game closed -> OFFLINE
    let account = service.syncAccountInfo({
      gameRunning: false,
      runtimeConnected: false,
    });
    assertStrictEqual(account.status, 'OFFLINE', 'Lifecycle Step 1: OFFLINE');
    assertStrictEqual(account.currentSessionPlayerId, null, 'Lifecycle Step 1: currentSessionPlayerId is null');

    // 2. Game launched, runtime connecting -> WAITING_RUNTIME
    account = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: false,
    });
    assertStrictEqual(account.status, 'WAITING_RUNTIME', 'Lifecycle Step 2: WAITING_RUNTIME');
    assert(account.status !== 'LOGIN_REQUIRED', 'Lifecycle Step 2: Prohibit LOGIN_REQUIRED');

    // 3. Runtime connected, account playerId null -> IDENTITY_PENDING
    account = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: null,
      roomId: null,
    });
    assertStrictEqual(account.status, 'IDENTITY_PENDING', 'Lifecycle Step 3: IDENTITY_PENDING');
    assert(account.status !== 'LOGIN_REQUIRED', 'Lifecycle Step 3: Prohibit LOGIN_REQUIRED when runtime connected');
    const uiText3 = formatSpectatorPlayerId(account.currentSessionPlayerId, account.runtimeConnected, true);
    assertStrictEqual(uiText3, '等待 Runtime 识别', 'Lifecycle Step 3: UI text is 等待 Runtime 识别');
    assert(!uiText3.includes('等待登录'), 'Lifecycle Step 3: UI text NEVER says 等待登录');

    // 4. Enter PVE match (Room 152953), payload received with spectatorAccountPlayerId: 1556514 -> IN_USE
    account = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: 1556514,
      displayName: '星枢观战号_Alpha',
      roomId: 152953,
    });
    assertStrictEqual(account.status, 'IN_USE', 'Lifecycle Step 4: IN_USE in room 152953');
    assertStrictEqual(account.currentSessionPlayerId, 1556514, 'Lifecycle Step 4: currentSessionPlayerId is 1556514');
    assertStrictEqual(account.playerId, 1556514, 'Lifecycle Step 4: playerId captured');
    assertStrictEqual(account.displayName, '星枢观战号_Alpha', 'Lifecycle Step 4: displayName captured');
    assertStrictEqual(account.lastKnownPlayerId, 1556514, 'Lifecycle Step 4: lastKnownPlayerId is 1556514');

    // 5. Match concludes, returns to lobby -> READY (since playerId is now known)
    account = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: null,
      roomId: null,
    });
    assertStrictEqual(account.status, 'READY', 'Lifecycle Step 5: Returned to READY');
    assertStrictEqual(account.currentSessionPlayerId, 1556514, 'Lifecycle Step 5: currentSessionPlayerId remains 1556514');
    assert(account.status !== 'LOGIN_REQUIRED', 'Lifecycle Step 5: Prohibit regression to LOGIN_REQUIRED');

    // 6. Game process closed -> OFFLINE
    account = service.syncAccountInfo({
      gameRunning: false,
      runtimeConnected: false,
    });
    assertStrictEqual(account.status, 'OFFLINE', 'Lifecycle Step 6: Closed game reverts to OFFLINE');
    assertStrictEqual(account.currentSessionPlayerId, null, 'Lifecycle Step 6: currentSessionPlayerId reset to null');
    assertStrictEqual(account.lastKnownPlayerId, 1556514, 'Lifecycle Step 6: lastKnownPlayerId preserved in storage');
  });

  // ---------------------------------------------------------------------------
  // Test Group 3: Zero Credential Storage Security Guarantee
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 3: Zero Credential Storage & Security Audit ---');

  runTest('Group 3: Storage key is strictly "xingshu_user_spectator_account"', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);
    assertStrictEqual(service.getStorageKey(), USER_SPECTATOR_ACCOUNT_STORAGE_KEY, 'Storage key matches constant');
    assertStrictEqual(USER_SPECTATOR_ACCOUNT_STORAGE_KEY, 'xingshu_user_spectator_account', 'Exact key name check');
  });

  runTest('Group 3: Valid metadata persists only safe non-credential fields', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    const saved = service.saveMetadata({
      platformId: 'STEAM_CN',
      playerId: 1556514,
      displayName: 'SpectatorBot_01',
      lastSeenAt: '2026-10-17T15:30:00.000Z',
    });

    assertStrictEqual(saved.platformId, 'STEAM_CN', 'Saved platformId');
    assertStrictEqual(saved.playerId, 1556514, 'Saved playerId');
    assertStrictEqual(saved.displayName, 'SpectatorBot_01', 'Saved displayName');

    const rawStored = memStorage.getItem(USER_SPECTATOR_ACCOUNT_STORAGE_KEY);
    assert(rawStored !== null, 'Raw storage item exists');

    const parsed = JSON.parse(rawStored!);
    const keys = Object.keys(parsed);
    const expectedKeys = ['platformId', 'lastKnownPlayerId', 'playerId', 'displayName', 'lastSeenAt'];

    assert(
      keys.every((k) => expectedKeys.includes(k)),
      `Stored JSON keys (${keys.join(', ')}) strictly conform to safe metadata whitelist`
    );

    // Verify zero forbidden substrings in raw stored JSON
    for (const forbidden of FORBIDDEN_SECURITY_KEYS) {
      assert(
        !rawStored!.toLowerCase().includes(forbidden),
        `Stored JSON does not contain forbidden keyword "${forbidden}"`
      );
    }
  });

  runTest('Group 3: Injection of credential keys throws SECURITY_VIOLATION and is blocked', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    const maliciousPayloads = [
      { password: 'super_secret_password', platformId: 'STEAM_CN' },
      { token: 'bearer_token_xyz', playerId: 1556514 },
      { auth_key: 'session_auth_cookie', displayName: 'Hacker' },
      { steamPassword: '123', platformId: 'STEAM_CN' },
      { credentials: { user: 'a', pass: 'b' } },
      { cookie: 'steamLoginSecure=1234' },
      { apiKey: 'sk-proj-12345' },
    ];

    for (const payload of maliciousPayloads) {
      let threw = false;
      let violationMsg = '';
      try {
        service.saveMetadata(payload as any);
      } catch (err: any) {
        threw = true;
        violationMsg = err.message;
      }

      assert(threw, `Malicious payload rejected: ${JSON.stringify(payload)}`);
      assert(violationMsg.includes('SECURITY_VIOLATION'), `Error message indicates security violation: ${violationMsg}`);
    }
  });

  runTest('Group 3: Static validateSecurityGuarantee detects forbidden fields accurately', () => {
    const auditPass = UserOwnedSpectatorAccountService.validateSecurityGuarantee({
      platformId: 'STEAM_CN',
      playerId: 1556514,
      displayName: 'SafeUser',
      lastSeenAt: '2026-10-17T12:00:00Z',
    });
    assert(auditPass.isSafe, 'Safe payload passes audit');
    assertStrictEqual(auditPass.violations.length, 0, 'No violations for clean payload');

    const auditFail = UserOwnedSpectatorAccountService.validateSecurityGuarantee({
      platformId: 'STEAM_CN',
      password: 'mypassword',
      authToken: 'token123',
    });
    assert(!auditFail.isSafe, 'Payload with credentials fails audit');
    assert(auditFail.violations.includes('password'), 'Violations detect password');
    assert(auditFail.violations.includes('authToken'), 'Violations detect authToken');
  });

  // ---------------------------------------------------------------------------
  // Test Group 4: Storage Persistence Reload and Auto-Sync
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 4: Storage Persistence & Reload ---');

  runTest('Group 4: Fresh instance correctly loads previously saved safe metadata', () => {
    const sharedStorage = new MemoryStorage();
    const service1 = new UserOwnedSpectatorAccountService(sharedStorage);

    service1.saveMetadata({
      platformId: 'STEAM_CN',
      lastKnownPlayerId: 1556514,
      displayName: '观战主号',
      lastSeenAt: '2026-10-17T14:20:00Z',
    });

    // Create a new independent service instance sharing same storage
    const service2 = new UserOwnedSpectatorAccountService(sharedStorage);
    const info = service2.getAccountInfo();

    assertStrictEqual(info.platformId, 'STEAM_CN', 'Reloaded platformId matches');
    assertStrictEqual(info.lastKnownPlayerId, 1556514, 'Reloaded lastKnownPlayerId matches');
    assertStrictEqual(info.currentSessionPlayerId, null, 'Fresh instance currentSessionPlayerId is strictly null');
    assertStrictEqual(info.playerId, null, 'Fresh instance playerId is strictly null');
    assertStrictEqual(info.displayName, '观战主号', 'Reloaded displayName matches');
    assertStrictEqual(info.lastSeenAt, '2026-10-17T14:20:00Z', 'Reloaded lastSeenAt matches');
    assertStrictEqual(info.status, SpectatorAccountStatus.OFFLINE, 'Initial status remains OFFLINE before runtime sync');
  });

  runTest('Group 4: Current Session Identity Truth Across Multi-Session Boundaries', () => {
    const sharedStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(sharedStorage);

    // Session A: Active match with authentic historical playerId 1556514
    let account = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: 1556514,
    });
    assertStrictEqual(account.status, 'READY', 'Session A is READY');
    assertStrictEqual(account.currentSessionPlayerId, 1556514, 'Session A currentSessionPlayerId is 1556514');
    assertStrictEqual(account.playerId, 1556514, 'Session A playerId is 1556514');
    assertStrictEqual(account.lastKnownPlayerId, 1556514, 'Session A lastKnownPlayerId is 1556514');

    // UI evaluation in Session A
    let uiText = formatSpectatorPlayerId(account.currentSessionPlayerId, account.runtimeConnected, true);
    assertStrictEqual(uiText, '1556514', 'UI displays authentic 1556514 in Session A');

    // Disconnect: Game closed / runtime disconnected
    account = service.syncAccountInfo({
      gameRunning: false,
      runtimeConnected: false,
    });
    assertStrictEqual(account.status, 'OFFLINE', 'Status reverts to OFFLINE');
    assertStrictEqual(account.currentSessionPlayerId, null, 'currentSessionPlayerId reset to null upon disconnect');
    assertStrictEqual(account.playerId, null, 'playerId reset to null upon disconnect');
    assertStrictEqual(account.lastKnownPlayerId, 1556514, 'lastKnownPlayerId remains preserved in storage');

    uiText = formatSpectatorPlayerId(account.currentSessionPlayerId, account.runtimeConnected, true);
    assertStrictEqual(uiText, '未识别', 'UI displays 未识别 when disconnected');

    // Session B: New game launched & Runtime connected, but NO playerId packet arrived yet
    account = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: null, // Pre-packet state!
    });
    assertStrictEqual(account.status, 'IDENTITY_PENDING', 'Session B initial state is IDENTITY_PENDING');
    assert(account.status !== 'LOGIN_REQUIRED', 'Session B initial state is strictly NOT LOGIN_REQUIRED');
    assertStrictEqual(account.currentSessionPlayerId, null, 'Session B currentSessionPlayerId MUST be strictly null');
    assertStrictEqual(account.playerId, null, 'Session B playerId MUST be strictly null');
    assertStrictEqual(account.lastKnownPlayerId, 1556514, 'lastKnownPlayerId is 1556514, but MUST NOT be promoted');

    // STRICT SPEC REQUIREMENT: Must show "等待 Runtime 识别", NEVER 1556514!
    uiText = formatSpectatorPlayerId(account.currentSessionPlayerId, account.runtimeConnected, true);
    assertStrictEqual(uiText, '等待 Runtime 识别', 'Session B pre-packet UI strictly displays 等待 Runtime 识别');
    assert(uiText !== '1556514', 'Session B pre-packet UI NEVER leaks past playerId 1556514');
    assert(!uiText.includes('等待登录'), 'Session B pre-packet UI does not say 等待登录');

    // Session B: Genuine payload arrives with new player ID 999999
    account = service.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: 999999,
    });
    assertStrictEqual(account.status, 'READY', 'Session B advances to READY');
    assertStrictEqual(account.currentSessionPlayerId, 999999, 'Session B currentSessionPlayerId is 999999');
    assertStrictEqual(account.playerId, 999999, 'Session B playerId is 999999');
    assertStrictEqual(account.lastKnownPlayerId, 999999, 'lastKnownPlayerId updated to 999999');

    uiText = formatSpectatorPlayerId(account.currentSessionPlayerId, account.runtimeConnected, true);
    assertStrictEqual(uiText, '999999', 'Session B UI displays 999999 once authentic payload arrives');
  });

  runTest('Group 4: clearSavedMetadata removes storage entry and resets account state', () => {
    const memStorage = new MemoryStorage();
    const service = new UserOwnedSpectatorAccountService(memStorage);

    service.saveMetadata({
      platformId: 'STEAM_CN',
      lastKnownPlayerId: 1556514,
      displayName: 'TempAccount',
    });

    service.clearSavedMetadata();

    assertStrictEqual(memStorage.getItem(USER_SPECTATOR_ACCOUNT_STORAGE_KEY), null, 'Storage item cleared');
    const info = service.getAccountInfo();
    assertStrictEqual(info.currentSessionPlayerId, null, 'In-memory currentSessionPlayerId cleared');
    assertStrictEqual(info.playerId, null, 'In-memory playerId cleared');
    assertStrictEqual(info.lastKnownPlayerId, null, 'In-memory lastKnownPlayerId cleared');
    assertStrictEqual(info.displayName, null, 'In-memory displayName cleared');
  });

  runTest('Group 4: Singleton userOwnedSpectatorAccountService is exported and initialized', () => {
    assert(userOwnedSpectatorAccountService !== undefined, 'Singleton is defined');
    assertStrictEqual(
      userOwnedSpectatorAccountService.getStorageKey(),
      USER_SPECTATOR_ACCOUNT_STORAGE_KEY,
      'Singleton storage key matches constant'
    );
  });

  // ---------------------------------------------------------------------------
  // Test Group 5: Launcher URI Protocol Triggers
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 5: Launcher URI Protocol Triggers ---');

  runTest('Group 5: getLaunchUri generates official steam protocol URI for 2622000', () => {
    const service = new UserOwnedSpectatorAccountService(new MemoryStorage());

    const uriSteamCn = service.getLaunchUri('STEAM_CN');
    assertStrictEqual(uriSteamCn, 'steam://run/2622000', 'STEAM_CN URI is steam://run/2622000');

    const uriSteamGlobal = service.getLaunchUri('STEAM_GLOBAL');
    assertStrictEqual(uriSteamGlobal, 'steam://run/2622000', 'STEAM_GLOBAL URI is steam://run/2622000');

    const uriDefault = service.getLaunchUri();
    assertStrictEqual(uriDefault, 'steam://run/2622000', 'Default URI is steam://run/2622000');
  });

  await runAsyncTest('Group 5: launchGame executes safely in headless environment', async () => {
    const service = new UserOwnedSpectatorAccountService(new MemoryStorage());
    const res = await service.launchGame('STEAM_CN');
    assertStrictEqual(res.uri, 'steam://run/2622000', 'launchGame returns target URI');
    assertStrictEqual(res.launched, false, 'launchGame does not pop windows in headless test environment');
  });

  // ---------------------------------------------------------------------------
  // Test Group 6: Static Code Analysis & Security Invariant Verification
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 6: Source Code Security Audit ---');

  runTest('Group 6: Service source code contains zero hardcoded passwords or dummy credentials', () => {
    // @ts-ignore
    const currentDir = path.dirname(fileURLToPath(import.meta.url));
    const servicePath = path.resolve(currentDir, '../services/spectatorAccountService.ts');
    const typesPath = path.resolve(currentDir, '../types/spectatorAccount.ts');

    assert(fs.existsSync(servicePath), 'spectatorAccountService.ts file exists');
    assert(fs.existsSync(typesPath), 'spectatorAccount.ts file exists');

    const serviceCode = fs.readFileSync(servicePath, 'utf8');
    const typesCode = fs.readFileSync(typesPath, 'utf8');
    const allCode = serviceCode + '\n' + typesCode;

    const prohibitedPatterns = [
      /password\s*[:=]\s*['"`][^'"`]+['"`]/i,
      /secret\s*[:=]\s*['"`][^'"`]+['"`]/i,
      /steam_password/i,
      /dummy_password/i,
      /fake_password/i,
      /mock_password/i,
      /auth_token\s*[:=]\s*['"`][^'"`]+['"`]/i,
      /private_key\s*[:=]/i,
    ];

    for (const pattern of prohibitedPatterns) {
      const match = pattern.exec(allCode);
      assert(
        match === null,
        `Zero hardcoded credential pattern: ${pattern.toString()}`,
        `Found forbidden pattern: ${match?.[0]}`
      );
    }

    const prohibitedSubstrings = [
      '123456',
      'password123',
      'admin123',
      'fake_steam_account',
      'mock_bot_account',
    ];

    for (const substr of prohibitedSubstrings) {
      assert(
        !allCode.includes(substr),
        `Source code does not contain prohibited dummy string '${substr}'`
      );
    }
  });

  // ---------------------------------------------------------------------------
  // Summary Report
  // ---------------------------------------------------------------------------
  console.log('\n================================================================================');
  console.log(`USER-OWNED SPECTATOR ACCOUNT RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
  console.log('================================================================================');

  if (failCount > 0) {
    process.exit(1);
  }
}

main().catch((err) => {
  console.error('Fatal test runner failure:', err);
  process.exit(1);
});
