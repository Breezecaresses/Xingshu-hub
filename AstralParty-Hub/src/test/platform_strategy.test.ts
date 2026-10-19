/**
 * Project XingShu — Platform Launch & Auth Strategy Test Suite
 * Phase: HS4-P1.4
 *
 * Verifies:
 * 1. 5 canonical supported platforms: STEAM_CN, STEAM_GLOBAL, TAPTAP, BILIBILI, WEGAME
 * 2. STEAM_CN and STEAM_GLOBAL resolve to LAUNCH_METHOD_STEAM_URI ('steam://run/2622000')
 * 3. TAPTAP, BILIBILI, WEGAME fail closed with LAUNCH_NOT_IMPLEMENTED (Unaudited launcher)
 * 4. STRICT RULE: If steamAppId == null, MUST NEVER fallback to steam://run/2622000!
 * 5. Unknown platform returns LAUNCH_UNSUPPORTED
 * 6. V1 PlatformAuthStrategy for all user-owned accounts is OFFICIAL_CLIENT_LOGIN (zero credentials)
 * 7. XINGSHU_MANAGED account mode remains honest SERVICE_NOT_CONFIGURED
 * 8. Zero password/secret/token collection patterns in source code
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  SUPPORTED_PLATFORMS,
  CANONICAL_ASTRAL_PARTY_STEAM_APP_ID,
  PlatformLaunchMethod,
  resolvePlatformLaunchStrategy,
  PlatformAuthPolicy,
  resolvePlatformAuthStrategy,
} from '../services/platformLaunchStrategy.ts';

import { SpectatorAccountStatus } from '../types/spectatorAccount.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.4: PLATFORM LAUNCH & AUTH STRATEGY TEST SUITE');
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

// ---------------------------------------------------------------------------
// Test Group 1: Supported Platforms Enumeration
// ---------------------------------------------------------------------------
console.log('\n--- Group 1: Canonical Supported Platforms ---');

runTest('Group 1: Exactly 5 supported platforms defined', () => {
  assertStrictEqual(SUPPORTED_PLATFORMS.length, 5, 'SUPPORTED_PLATFORMS.length === 5');
  assert(SUPPORTED_PLATFORMS.includes('STEAM_CN'), 'Includes STEAM_CN');
  assert(SUPPORTED_PLATFORMS.includes('STEAM_GLOBAL'), 'Includes STEAM_GLOBAL');
  assert(SUPPORTED_PLATFORMS.includes('TAPTAP'), 'Includes TAPTAP');
  assert(SUPPORTED_PLATFORMS.includes('BILIBILI'), 'Includes BILIBILI');
  assert(SUPPORTED_PLATFORMS.includes('WEGAME'), 'Includes WEGAME');
});

runTest('Group 1: Canonical Steam App ID is 2622000', () => {
  assertStrictEqual(CANONICAL_ASTRAL_PARTY_STEAM_APP_ID, 2622000, 'CANONICAL_ASTRAL_PARTY_STEAM_APP_ID === 2622000');
});

// ---------------------------------------------------------------------------
// Test Group 2: Steam CN & Steam Global Launch Strategy
// ---------------------------------------------------------------------------
console.log('\n--- Group 2: Steam Platforms Launch Resolution ---');

runTest('Group 2: STEAM_CN resolves to LAUNCH_METHOD_STEAM_URI', () => {
  const plan = resolvePlatformLaunchStrategy('STEAM_CN');
  assertStrictEqual(plan.isSupported, true, 'STEAM_CN isSupported');
  assertStrictEqual(plan.canLaunch, true, 'STEAM_CN canLaunch');
  assertStrictEqual(plan.launchMethod, PlatformLaunchMethod.LAUNCH_METHOD_STEAM_URI, 'method is STEAM_URI');
  assertStrictEqual(plan.steamAppId, 2622000, 'steamAppId === 2622000');
  assertStrictEqual(plan.uri, 'steam://run/2622000', 'uri === steam://run/2622000');
  assertStrictEqual(plan.status, 'READY', 'status === READY');
  assertStrictEqual(plan.reason, null, 'reason === null');
});

runTest('Group 2: STEAM_GLOBAL resolves to LAUNCH_METHOD_STEAM_URI', () => {
  const plan = resolvePlatformLaunchStrategy('STEAM_GLOBAL');
  assertStrictEqual(plan.isSupported, true, 'STEAM_GLOBAL isSupported');
  assertStrictEqual(plan.canLaunch, true, 'STEAM_GLOBAL canLaunch');
  assertStrictEqual(plan.launchMethod, PlatformLaunchMethod.LAUNCH_METHOD_STEAM_URI, 'method is STEAM_URI');
  assertStrictEqual(plan.steamAppId, 2622000, 'steamAppId === 2622000');
  assertStrictEqual(plan.uri, 'steam://run/2622000', 'uri === steam://run/2622000');
  assertStrictEqual(plan.status, 'READY', 'status === READY');
  assertStrictEqual(plan.reason, null, 'reason === null');
});

// ---------------------------------------------------------------------------
// Test Group 3: Non-Steam Platforms Fail-Closed (LAUNCH_NOT_IMPLEMENTED)
// ---------------------------------------------------------------------------
console.log('\n--- Group 3: Non-Steam Fail-Closed Enforcement ---');

runTest('Group 3: TAPTAP fails closed with LAUNCH_NOT_IMPLEMENTED', () => {
  const plan = resolvePlatformLaunchStrategy('TAPTAP');
  assertStrictEqual(plan.isSupported, true, 'TAPTAP is supported in schema');
  assertStrictEqual(plan.canLaunch, false, 'TAPTAP canLaunch is strictly false');
  assertStrictEqual(plan.launchMethod, PlatformLaunchMethod.LAUNCH_NOT_IMPLEMENTED, 'method is LAUNCH_NOT_IMPLEMENTED');
  assertStrictEqual(plan.uri, null, 'uri is strictly null');
  assertStrictEqual(plan.steamAppId, null, 'steamAppId is strictly null');
  assertStrictEqual(plan.status, 'LAUNCH_NOT_IMPLEMENTED', 'status is LAUNCH_NOT_IMPLEMENTED');
  assertStrictEqual(plan.reason, 'Unaudited launcher', 'reason is Unaudited launcher');
});

runTest('Group 3: BILIBILI fails closed with LAUNCH_NOT_IMPLEMENTED', () => {
  const plan = resolvePlatformLaunchStrategy('BILIBILI');
  assertStrictEqual(plan.isSupported, true, 'BILIBILI is supported in schema');
  assertStrictEqual(plan.canLaunch, false, 'BILIBILI canLaunch is strictly false');
  assertStrictEqual(plan.launchMethod, PlatformLaunchMethod.LAUNCH_NOT_IMPLEMENTED, 'method is LAUNCH_NOT_IMPLEMENTED');
  assertStrictEqual(plan.uri, null, 'uri is strictly null');
  assertStrictEqual(plan.steamAppId, null, 'steamAppId is strictly null');
  assertStrictEqual(plan.status, 'LAUNCH_NOT_IMPLEMENTED', 'status is LAUNCH_NOT_IMPLEMENTED');
  assertStrictEqual(plan.reason, 'Unaudited launcher', 'reason is Unaudited launcher');
});

runTest('Group 3: WEGAME fails closed with LAUNCH_NOT_IMPLEMENTED', () => {
  const plan = resolvePlatformLaunchStrategy('WEGAME');
  assertStrictEqual(plan.isSupported, true, 'WEGAME is supported in schema');
  assertStrictEqual(plan.canLaunch, false, 'WEGAME canLaunch is strictly false');
  assertStrictEqual(plan.launchMethod, PlatformLaunchMethod.LAUNCH_NOT_IMPLEMENTED, 'method is LAUNCH_NOT_IMPLEMENTED');
  assertStrictEqual(plan.uri, null, 'uri is strictly null');
  assertStrictEqual(plan.steamAppId, null, 'steamAppId is strictly null');
  assertStrictEqual(plan.status, 'LAUNCH_NOT_IMPLEMENTED', 'status is LAUNCH_NOT_IMPLEMENTED');
  assertStrictEqual(plan.reason, 'Unaudited launcher', 'reason is Unaudited launcher');
});

// ---------------------------------------------------------------------------
// Test Group 4: STRICT RULE - Never Fallback to Steam if steamAppId is null
// ---------------------------------------------------------------------------
console.log('\n--- Group 4: Strict No-Steam-Fallback Invariant ---');

runTest('Group 4: Non-steam platforms MUST NEVER evaluate to steam:// URI', () => {
  const nonSteamPlatforms = ['TAPTAP', 'BILIBILI', 'WEGAME'];
  for (const pid of nonSteamPlatforms) {
    const plan = resolvePlatformLaunchStrategy(pid);
    assert(plan.uri !== 'steam://run/2622000', `${pid} URI MUST NOT be steam://run/2622000`);
    assert(plan.uri === null, `${pid} URI MUST be strictly null`);
    assert(plan.steamAppId === null, `${pid} steamAppId MUST be strictly null`);
  }
});

// ---------------------------------------------------------------------------
// Test Group 5: Unsupported / Unknown Platforms
// ---------------------------------------------------------------------------
console.log('\n--- Group 5: Unsupported Platforms ---');

runTest('Group 5: EPIC_GAMES returns LAUNCH_UNSUPPORTED', () => {
  const plan = resolvePlatformLaunchStrategy('EPIC_GAMES');
  assertStrictEqual(plan.isSupported, false, 'EPIC_GAMES isSupported === false');
  assertStrictEqual(plan.canLaunch, false, 'EPIC_GAMES canLaunch === false');
  assertStrictEqual(plan.launchMethod, PlatformLaunchMethod.LAUNCH_UNSUPPORTED, 'method is LAUNCH_UNSUPPORTED');
  assertStrictEqual(plan.uri, null, 'uri === null');
  assertStrictEqual(plan.status, 'LAUNCH_UNSUPPORTED', 'status === LAUNCH_UNSUPPORTED');
});

runTest('Group 5: Empty platform string returns LAUNCH_UNSUPPORTED', () => {
  const plan = resolvePlatformLaunchStrategy('');
  assertStrictEqual(plan.isSupported, false, 'empty platform isSupported === false');
  assertStrictEqual(plan.canLaunch, false, 'empty platform canLaunch === false');
  assertStrictEqual(plan.launchMethod, PlatformLaunchMethod.LAUNCH_UNSUPPORTED, 'method is LAUNCH_UNSUPPORTED');
});

// ---------------------------------------------------------------------------
// Test Group 6: Platform Authentication Strategy (V1 OFFICIAL_CLIENT_LOGIN)
// ---------------------------------------------------------------------------
console.log('\n--- Group 6: Platform Authentication Strategy ---');

runTest('Group 6: All 5 platforms under USER_OWNED follow OFFICIAL_CLIENT_LOGIN', () => {
  for (const pid of SUPPORTED_PLATFORMS) {
    const auth = resolvePlatformAuthStrategy(pid, 'USER_OWNED');
    assertStrictEqual(auth.authPolicy, PlatformAuthPolicy.OFFICIAL_CLIENT_LOGIN, `${pid} auth policy is OFFICIAL_CLIENT_LOGIN`);
    assertStrictEqual(auth.requiresCredentials, false, `${pid} requiresCredentials === false`);
    assertStrictEqual(auth.status, SpectatorAccountStatus.AVAILABLE, `${pid} status === AVAILABLE`);
    assert(auth.flowSteps.length >= 4, `${pid} flowSteps documented`);
    assert(auth.securityAssurance.includes('零凭据'), `${pid} securityAssurance verifies zero credentials`);
  }
});

runTest('Group 6: XINGSHU_MANAGED mode resolves to SERVICE_NOT_CONFIGURED', () => {
  const auth = resolvePlatformAuthStrategy('STEAM_CN', 'XINGSHU_MANAGED');
  assertStrictEqual(auth.authPolicy, PlatformAuthPolicy.SERVICE_NOT_CONFIGURED, 'Managed authPolicy is SERVICE_NOT_CONFIGURED');
  assertStrictEqual(auth.requiresCredentials, false, 'Managed requiresCredentials is false');
  assertStrictEqual(auth.status, SpectatorAccountStatus.SERVICE_NOT_CONFIGURED, 'Managed status is SERVICE_NOT_CONFIGURED');
  assert(auth.userMessage.includes('尚未配置') || auth.userMessage.includes('未配置'), 'User message indicates service not configured');
});

// ---------------------------------------------------------------------------
// Test Group 7: Source Code Security Audit
// ---------------------------------------------------------------------------
console.log('\n--- Group 7: Source Code Security Audit ---');

runTest('Group 7: platformLaunchStrategy.ts has zero credential collection patterns', () => {
  // @ts-ignore
  const thisFile = fileURLToPath(import.meta.url);
  // @ts-ignore
  const thisDir = path.dirname(thisFile);
  const targetFile = path.resolve(thisDir, '../services/platformLaunchStrategy.ts');
  const content = fs.readFileSync(targetFile, 'utf-8');

  const forbidden = [
    /password\s*[:=]\s*['"`][^'"`]+['"`]/i,
    /token\s*[:=]\s*['"`][^'"`]+['"`]/i,
    /secret\s*[:=]\s*['"`][^'"`]+['"`]/i,
    /cookie\s*[:=]\s*['"`][^'"`]+['"`]/i,
  ];

  for (const pat of forbidden) {
    assert(!pat.test(content), `platformLaunchStrategy.ts clean of pattern ${pat}`);
  }
});

console.log('================================================================================');
console.log(`PLATFORM LAUNCH & AUTH STRATEGY RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
console.log('================================================================================');

if (failCount > 0) {
  process.exit(1);
}
