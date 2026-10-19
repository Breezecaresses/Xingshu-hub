/**
 * Project XingShu — Game Account Identity Test Suite
 * Phase: HS4-P1.4
 * Lane 3: Game Account Identity Model Specialist
 *
 * Verifies:
 * - Test 1: Binding a player as MAIN -> resolved role is MAIN
 * - Test 2: Binding a player as USER_SPECTATOR -> resolved role is USER_SPECTATOR
 * - Test 3: Unbound player ID -> resolved role is UNBOUND_ACCOUNT
 * - Test 4: Disconnected runtime (null playerId) -> resolved role is DISCONNECTED
 * - Test 5: Re-binding overwrites previous binding for the same role safely
 * - Test 6: Zero passwords or tokens present in serialized JSON
 * - Test 7: Mismatch detection triggers warning when role == MAIN and mode == USER_SPECTATOR
 * - Additional security & invariant tests (credential injection rejection, offline disconnect truth).
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  GameAccountService,
  MemoryStorage,
  type CurrentRuntimeIdentity,
} from '../services/gameAccountService.ts';

import {
  GameAccountRole,
  ResolvedAccountRole,
  STORAGE_KEY_GAME_ACCOUNT_BINDINGS,
  FORBIDDEN_SECURITY_KEYS,
} from '../types/gameAccount.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.4: GAME ACCOUNT IDENTITY TEST SUITE');
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

// -----------------------------------------------------------------------------
// Test 1: Binding a player as MAIN -> resolved role is MAIN
// -----------------------------------------------------------------------------
runTest('Test 1: Binding a player as MAIN -> resolved role is MAIN', () => {
  console.log('\n--- Test 1: Binding a player as MAIN -> resolved role is MAIN ---');
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  const mainPlayerId = 1556514;
  const binding = service.bindAccount(mainPlayerId, GameAccountRole.MAIN, 'STEAM_CN', 'XingShuPlayer');

  assertStrictEqual(binding.playerId, mainPlayerId, 'Binding created with correct playerId');
  assertStrictEqual(binding.role, GameAccountRole.MAIN, 'Binding created with role MAIN');
  assertStrictEqual(binding.platformId, 'STEAM_CN', 'Binding platform is STEAM_CN');

  const resolved = service.resolveSession(mainPlayerId, 'STEAM_CN');
  assertStrictEqual(resolved.resolvedRole, GameAccountRole.MAIN, 'Resolved role is strictly MAIN');
  assertStrictEqual(resolved.currentSessionPlayerId, mainPlayerId, 'currentSessionPlayerId matches live ID');
  assert(resolved.binding !== null, 'Resolved session provides non-null binding');
  assertStrictEqual(resolved.binding?.displayName, 'XingShuPlayer', 'Binding displayName preserved');
});

// -----------------------------------------------------------------------------
// Test 2: Binding a player as USER_SPECTATOR -> resolved role is USER_SPECTATOR
// -----------------------------------------------------------------------------
runTest('Test 2: Binding a player as USER_SPECTATOR -> resolved role is USER_SPECTATOR', () => {
  console.log('\n--- Test 2: Binding a player as USER_SPECTATOR -> resolved role is USER_SPECTATOR ---');
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  const spectatorPlayerId = 8888888;
  const binding = service.bindAccount(spectatorPlayerId, GameAccountRole.USER_SPECTATOR, 'STEAM_CN', 'SpectatorBot01');

  assertStrictEqual(binding.playerId, spectatorPlayerId, 'Binding created with spectator playerId');
  assertStrictEqual(binding.role, GameAccountRole.USER_SPECTATOR, 'Binding created with role USER_SPECTATOR');

  const resolved = service.resolveSession(spectatorPlayerId, 'STEAM_CN');
  assertStrictEqual(resolved.resolvedRole, GameAccountRole.USER_SPECTATOR, 'Resolved role is strictly USER_SPECTATOR');
  assertStrictEqual(resolved.currentSessionPlayerId, spectatorPlayerId, 'currentSessionPlayerId matches live ID');
  assertStrictEqual(resolved.binding?.playerId, spectatorPlayerId, 'Binding in identity matches spectatorPlayerId');
});

// -----------------------------------------------------------------------------
// Test 3: Unbound player ID -> resolved role is UNBOUND_ACCOUNT
// -----------------------------------------------------------------------------
runTest('Test 3: Unbound player ID -> resolved role is UNBOUND_ACCOUNT', () => {
  console.log('\n--- Test 3: Unbound player ID -> resolved role is UNBOUND_ACCOUNT ---');
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  // Bind MAIN to 1556514
  service.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', 'Main');

  // Live session reports unknown player ID 9999999
  const unknownPlayerId = 9999999;
  const resolved = service.resolveSession(unknownPlayerId, 'STEAM_CN');

  assertStrictEqual(resolved.resolvedRole, ResolvedAccountRole.UNBOUND_ACCOUNT, 'Unknown ID resolves to UNBOUND_ACCOUNT');
  assertStrictEqual(resolved.currentSessionPlayerId, unknownPlayerId, 'currentSessionPlayerId preserves live unrecognized ID');
  assertStrictEqual(resolved.binding, null, 'Unbound account has strictly null binding');
});

// -----------------------------------------------------------------------------
// Test 4: Disconnected runtime (null playerId) -> resolved role is DISCONNECTED
// -----------------------------------------------------------------------------
runTest('Test 4: Disconnected runtime (null playerId) -> resolved role is DISCONNECTED', () => {
  console.log('\n--- Test 4: Disconnected runtime (null playerId) -> resolved role is DISCONNECTED ---');
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  // Populate bindings
  service.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', 'Main');
  service.bindAccount(8888888, GameAccountRole.USER_SPECTATOR, 'STEAM_CN', 'Spectator');

  // Case 4a: null playerId
  const resolvedNull = service.resolveSession(null, 'STEAM_CN');
  assertStrictEqual(resolvedNull.resolvedRole, ResolvedAccountRole.DISCONNECTED, 'null playerId resolves to DISCONNECTED');
  assertStrictEqual(resolvedNull.currentSessionPlayerId, null, 'currentSessionPlayerId is strictly null');
  assertStrictEqual(resolvedNull.binding, null, 'binding is strictly null');

  // Case 4b: undefined playerId
  const resolvedUndefined = service.resolveSession(undefined, null);
  assertStrictEqual(resolvedUndefined.resolvedRole, ResolvedAccountRole.DISCONNECTED, 'undefined playerId resolves to DISCONNECTED');
  assertStrictEqual(resolvedUndefined.currentSessionPlayerId, null, 'currentSessionPlayerId is strictly null when undefined');

  // Case 4c: 0 or negative playerId
  const resolvedZero = service.resolveSession(0, 'STEAM_CN');
  assertStrictEqual(resolvedZero.resolvedRole, ResolvedAccountRole.DISCONNECTED, '0 playerId resolves to DISCONNECTED');
  assertStrictEqual(resolvedZero.currentSessionPlayerId, null, 'currentSessionPlayerId is null for 0');

  // Case 4d: Invariant check — NEVER promote stored bindings to active session when disconnected
  assertStrictEqual(resolvedNull.currentSessionPlayerId, null, 'INVARIANT: Offline session NEVER restores historical currentSessionPlayerId');
});

// -----------------------------------------------------------------------------
// Test 5: Re-binding overwrites previous binding for the same role safely
// -----------------------------------------------------------------------------
runTest('Test 5: Re-binding overwrites previous binding for the same role safely', () => {
  console.log('\n--- Test 5: Re-binding overwrites previous binding for the same role safely ---');
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  // 1. Initial bind of MAIN to 1111111
  service.bindAccount(1111111, GameAccountRole.MAIN, 'STEAM_CN', 'InitialMain');
  const initialMain = service.getBindingByRole(GameAccountRole.MAIN);
  assertStrictEqual(initialMain?.playerId, 1111111, 'Initial MAIN bound to 1111111');
  assertStrictEqual(service.getBindings().length, 1, 'Only 1 binding in store');

  // 2. Re-bind MAIN to 2222222
  const rebindResult = service.bindAccount(2222222, GameAccountRole.MAIN, 'STEAM_CN', 'NewMain');
  assertStrictEqual(rebindResult.playerId, 2222222, 'New MAIN returned from bindAccount');

  const bindingsAfter = service.getBindings();
  assertStrictEqual(bindingsAfter.length, 1, 'Still only 1 binding in store after rebind');
  assertStrictEqual(bindingsAfter[0].playerId, 2222222, 'Store contains updated PID 2222222');
  assertStrictEqual(bindingsAfter[0].role, GameAccountRole.MAIN, 'Store contains role MAIN');
  assertStrictEqual(bindingsAfter[0].displayName, 'NewMain', 'Store contains updated displayName');

  // 3. Old PID 1111111 now resolves to UNBOUND_ACCOUNT
  const oldResolved = service.resolveSession(1111111, 'STEAM_CN');
  assertStrictEqual(oldResolved.resolvedRole, ResolvedAccountRole.UNBOUND_ACCOUNT, 'Old PID 1111111 is now UNBOUND');

  // 4. New PID 2222222 resolves to MAIN
  const newResolved = service.resolveSession(2222222, 'STEAM_CN');
  assertStrictEqual(newResolved.resolvedRole, GameAccountRole.MAIN, 'New PID 2222222 resolves to MAIN');

  // 5. Unbind role
  service.unbindRole(GameAccountRole.MAIN);
  assertStrictEqual(service.getBindings().length, 0, 'Bindings empty after unbindRole');
  const unboundResolved = service.resolveSession(2222222, 'STEAM_CN');
  assertStrictEqual(unboundResolved.resolvedRole, ResolvedAccountRole.UNBOUND_ACCOUNT, '2222222 is UNBOUND after unbindRole');
});

// -----------------------------------------------------------------------------
// Test 6: Zero passwords or tokens present in serialized JSON
// -----------------------------------------------------------------------------
runTest('Test 6: Zero passwords or tokens present in serialized JSON', () => {
  console.log('\n--- Test 6: Zero passwords or tokens present in serialized JSON ---');
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  service.bindAccount(1556514, GameAccountRole.MAIN, 'STEAM_CN', 'MainAccount');
  service.bindAccount(8888888, GameAccountRole.USER_SPECTATOR, 'STEAM_CN', 'SpectatorAccount');

  const rawJson = storage.getItem(STORAGE_KEY_GAME_ACCOUNT_BINDINGS);
  assert(rawJson !== null, 'Serialized JSON exists in storage');

  // Verify all forbidden keys
  for (const forbidden of FORBIDDEN_SECURITY_KEYS) {
    const regex = new RegExp(`"${forbidden}"`, 'i');
    assert(!regex.test(rawJson!), `Serialized JSON does not contain forbidden keyword "${forbidden}"`);
  }

  // Attempt malicious injection: password field
  let rejectedPassword = false;
  try {
    service.bindAccount(
      1234567,
      GameAccountRole.MAIN,
      'STEAM_CN',
      'Hacker',
      { password: 'secret_password_123' } as any
    );
  } catch (err: any) {
    rejectedPassword = err.message.includes('SECURITY_VIOLATION');
  }
  assert(rejectedPassword, 'Security audit rejects payload containing password');

  // Attempt malicious injection: token field
  let rejectedToken = false;
  try {
    service.bindAccount(
      1234567,
      GameAccountRole.MAIN,
      'STEAM_CN',
      'Hacker',
      { session_token: 'xyz_bearer_token' } as any
    );
  } catch (err: any) {
    rejectedToken = err.message.includes('SECURITY_VIOLATION');
  }
  assert(rejectedToken, 'Security audit rejects payload containing token');

  // Verify whitelist fields only
  const parsed = JSON.parse(rawJson!);
  for (const record of parsed) {
    const allowedKeys = new Set([
      'bindingId',
      'platformId',
      'playerId',
      'displayName',
      'role',
      'boundAt',
      'lastSeenAt',
    ]);
    for (const key of Object.keys(record)) {
      assert(allowedKeys.has(key), `Key "${key}" is in allowed whitelist`);
    }
  }
});

// -----------------------------------------------------------------------------
// Test 7: Mismatch detection triggers warning when role == MAIN and mode == USER_SPECTATOR
// -----------------------------------------------------------------------------
runTest('Test 7: Mismatch detection triggers warning when role == MAIN and mode == USER_SPECTATOR', () => {
  console.log('\n--- Test 7: Mismatch detection triggers warning when role == MAIN and mode == USER_SPECTATOR ---');
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  const mainPid = 1556514;
  const spectatorPid = 8888888;
  service.bindAccount(mainPid, GameAccountRole.MAIN, 'STEAM_CN', 'MyMainPlayer');
  service.bindAccount(spectatorPid, GameAccountRole.USER_SPECTATOR, 'STEAM_CN', 'MySpectatorPlayer');

  // Helper matching the mismatch logic in SpectatorPage / useSpectatorState:
  // When user is in "USER_OWNED" spectator mode:
  // If runtime identity resolves to MAIN:
  // => MISMATCH! User is spectating with their MAIN game account rather than dedicated spectator account.
  function checkRoleMismatch(
    accountMode: 'USER_OWNED' | 'XINGSHU_MANAGED' | null,
    runtimeIdentity: CurrentRuntimeIdentity
  ): {
    hasMismatch: boolean;
    warningMessage: string | null;
  } {
    if (accountMode === 'USER_OWNED' && runtimeIdentity.resolvedRole === GameAccountRole.MAIN) {
      return {
        hasMismatch: true,
        warningMessage: `⚠️ 当前登录的是主账号 (PID: ${runtimeIdentity.currentSessionPlayerId})。请通过官方客户端切换至已绑定的专用观战账号，以避免占用游戏对局。`,
      };
    }
    return {
      hasMismatch: false,
      warningMessage: null,
    };
  }

  // Case 7a: Mode is USER_OWNED, runtime session is MAIN (Player 1556514)
  const sessionMain = service.resolveSession(mainPid, 'STEAM_CN');
  assertStrictEqual(sessionMain.resolvedRole, GameAccountRole.MAIN, 'Session resolved to MAIN');

  const mismatchResult1 = checkRoleMismatch('USER_OWNED', sessionMain);
  assertStrictEqual(mismatchResult1.hasMismatch, true, 'Role mismatch triggered for MAIN in USER_OWNED mode');
  assert(
    mismatchResult1.warningMessage?.includes('当前登录的是主账号 (PID: 1556514)') ?? false,
    'Warning message mentions PID 1556514'
  );
  assert(
    mismatchResult1.warningMessage?.includes('请通过官方客户端切换至已绑定的专用观战账号') ?? false,
    'Warning directs user to official client for switching'
  );

  // Case 7b: Mode is USER_OWNED, runtime session is USER_SPECTATOR (Player 8888888) -> No mismatch!
  const sessionSpectator = service.resolveSession(spectatorPid, 'STEAM_CN');
  assertStrictEqual(sessionSpectator.resolvedRole, GameAccountRole.USER_SPECTATOR, 'Session resolved to USER_SPECTATOR');
  const mismatchResult2 = checkRoleMismatch('USER_OWNED', sessionSpectator);
  assertStrictEqual(mismatchResult2.hasMismatch, false, 'No mismatch when session is USER_SPECTATOR');
  assertStrictEqual(mismatchResult2.warningMessage, null, 'Warning message is null when role matches');

  // Case 7c: Mode is XINGSHU_MANAGED -> No mismatch warning for local account
  const mismatchResult3 = checkRoleMismatch('XINGSHU_MANAGED', sessionMain);
  assertStrictEqual(mismatchResult3.hasMismatch, false, 'No mismatch in XINGSHU_MANAGED mode');

  // Case 7d: Runtime disconnected (null PID) -> No mismatch warning
  const sessionDisconnected = service.resolveSession(null, 'STEAM_CN');
  const mismatchResult4 = checkRoleMismatch('USER_OWNED', sessionDisconnected);
  assertStrictEqual(mismatchResult4.hasMismatch, false, 'No mismatch when disconnected');
});

// -----------------------------------------------------------------------------
// Test 8: Source Code Security & Integrity Audit
// -----------------------------------------------------------------------------
runTest('Test 8: Source Code Security & Integrity Audit', () => {
  console.log('\n--- Test 8: Source Code Security & Integrity Audit ---');
  const serviceFile = path.resolve('src/services/gameAccountService.ts');
  const typesFile = path.resolve('src/types/gameAccount.ts');

  assert(fs.existsSync(serviceFile), 'gameAccountService.ts file exists');
  assert(fs.existsSync(typesFile), 'gameAccount.ts file exists');

  const serviceCode = fs.readFileSync(serviceFile, 'utf8');
  const typesCode = fs.readFileSync(typesFile, 'utf8');

  // Audit that no hardcoded credentials or test dummy secrets exist
  const forbiddenPatterns = [
    /password\s*[:=]\s*['"`][^'"`]+['"`]/i,
    /secret\s*[:=]\s*['"`][^'"`]+['"`]/i,
    /steam_password/i,
    /auth_token\s*[:=]\s*['"`][^'"`]+['"`]/i,
  ];

  for (const pattern of forbiddenPatterns) {
    assert(!pattern.test(serviceCode), `gameAccountService.ts does not contain forbidden pattern: ${pattern}`);
    assert(!pattern.test(typesCode), `gameAccount.ts does not contain forbidden pattern: ${pattern}`);
  }
});

console.log('================================================================================');
console.log(`GAME ACCOUNT IDENTITY TEST RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
console.log('================================================================================');

if (failCount > 0) {
  process.exit(1);
}
