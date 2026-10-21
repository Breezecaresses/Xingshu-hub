/**
 * Project XingShu — HS4-P1.2.1 Canonical Steam App Identity Test Suite
 *
 * Verifies:
 * 1. Canonical JSON profile STEAM_CN has steamAppId === 2622000.
 * 2. Canonical JSON profile STEAM_GLOBAL has steamAppId === 2622000.
 * 3. Non-steam profiles (TAPTAP, BILIBILI, WEGAME) have steamAppId === null.
 * 4. getSteamLaunchUri() returns steam://run/2622000.
 * 5. Scans production source files verifying ZERO occurrences of prohibited App ID 2514700.
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import rawPlatformProfiles from '../../src-tauri/src/xing_shu/platform/platform_profiles.json' with { type: 'json' };
import {
  PlatformId,
  getDefaultPlatformProfile,
  getPlatformProfile,
} from '../types/platform.ts';

import {
  CANONICAL_ASTRAL_PARTY_STEAM_APP_ID,
  getSteamLaunchUri,
  UserOwnedSpectatorAccountService,
} from '../services/spectatorAccountService.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.2.1: CANONICAL STEAM APP IDENTITY TEST SUITE');
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

function main() {
  // ---------------------------------------------------------------------------
  // Check 1: Canonical JSON profile STEAM_CN has steamAppId === 2622000
  // ---------------------------------------------------------------------------
  console.log('\n--- Check 1: Canonical Profile STEAM_CN Steam App ID ---');

  runTest('Check 1: raw platform_profiles.json STEAM_CN has steamAppId === 2622000', () => {
    const steamCnRaw = rawPlatformProfiles.find((p: any) => p.platformId === 'STEAM_CN');
    assert(steamCnRaw !== undefined, 'STEAM_CN found in raw JSON');
    assertStrictEqual(steamCnRaw?.steamAppId, 2622000, 'raw JSON STEAM_CN steamAppId is 2622000');
  });

  runTest('Check 1: getDefaultPlatformProfile(STEAM_CN) has steamAppId === 2622000', () => {
    const steamCn = getDefaultPlatformProfile(PlatformId.STEAM_CN);
    assert(steamCn !== undefined, 'STEAM_CN found via getDefaultPlatformProfile');
    assertStrictEqual(steamCn?.steamAppId, 2622000, 'STEAM_CN profile steamAppId === 2622000');
  });

  runTest('Check 1: getPlatformProfile(STEAM_CN) has steamAppId === 2622000', () => {
    const steamCn = getPlatformProfile(PlatformId.STEAM_CN);
    assert(steamCn !== undefined, 'STEAM_CN found via getPlatformProfile');
    assertStrictEqual(steamCn?.steamAppId, 2622000, 'STEAM_CN profile steamAppId === 2622000');
  });

  // ---------------------------------------------------------------------------
  // Check 2: Canonical JSON profile STEAM_GLOBAL has steamAppId === 2622000
  // ---------------------------------------------------------------------------
  console.log('\n--- Check 2: Canonical Profile STEAM_GLOBAL Steam App ID ---');

  runTest('Check 2: raw platform_profiles.json STEAM_GLOBAL has steamAppId === 2622000', () => {
    const steamGlobalRaw = rawPlatformProfiles.find((p: any) => p.platformId === 'STEAM_GLOBAL');
    assert(steamGlobalRaw !== undefined, 'STEAM_GLOBAL found in raw JSON');
    assertStrictEqual(steamGlobalRaw?.steamAppId, 2622000, 'raw JSON STEAM_GLOBAL steamAppId is 2622000');
  });

  runTest('Check 2: getDefaultPlatformProfile(STEAM_GLOBAL) has steamAppId === 2622000', () => {
    const steamGlobal = getDefaultPlatformProfile(PlatformId.STEAM_GLOBAL);
    assert(steamGlobal !== undefined, 'STEAM_GLOBAL found via getDefaultPlatformProfile');
    assertStrictEqual(steamGlobal?.steamAppId, 2622000, 'STEAM_GLOBAL profile steamAppId === 2622000');
  });

  // ---------------------------------------------------------------------------
  // Check 3: Non-steam profiles have steamAppId === null
  // ---------------------------------------------------------------------------
  console.log('\n--- Check 3: Non-Steam Profiles Have steamAppId === null ---');

  const nonSteamIds: PlatformId[] = [PlatformId.TAPTAP, PlatformId.BILIBILI, PlatformId.WEGAME];
  for (const id of nonSteamIds) {
    runTest(`Check 3: raw platform_profiles.json ${id} has steamAppId === null`, () => {
      const raw = rawPlatformProfiles.find((p: any) => p.platformId === id);
      assert(raw !== undefined, `${id} found in raw JSON`);
      assertStrictEqual(raw?.steamAppId, null, `raw JSON ${id} steamAppId is null`);
    });

    runTest(`Check 3: getDefaultPlatformProfile(${id}) has steamAppId === null`, () => {
      const profile = getDefaultPlatformProfile(id);
      assert(profile !== undefined, `${id} found via getDefaultPlatformProfile`);
      assertStrictEqual(profile?.steamAppId, null, `${id} profile steamAppId === null`);
    });
  }

  // ---------------------------------------------------------------------------
  // Check 4: getSteamLaunchUri() returns steam://run/2622000
  // ---------------------------------------------------------------------------
  console.log('\n--- Check 4: getSteamLaunchUri Helper & SSOT Resolution ---');

  runTest('Check 4: CANONICAL_ASTRAL_PARTY_STEAM_APP_ID constant === 2622000', () => {
    assertStrictEqual(CANONICAL_ASTRAL_PARTY_STEAM_APP_ID, 2622000, 'CANONICAL_ASTRAL_PARTY_STEAM_APP_ID === 2622000');
  });

  runTest('Check 4: getSteamLaunchUri() default returns steam://run/2622000', () => {
    const uri = getSteamLaunchUri();
    assertStrictEqual(uri, 'steam://run/2622000', 'getSteamLaunchUri() === steam://run/2622000');
  });

  runTest('Check 4: getSteamLaunchUri(CANONICAL_ASTRAL_PARTY_STEAM_APP_ID) returns steam://run/2622000', () => {
    const uri = getSteamLaunchUri(CANONICAL_ASTRAL_PARTY_STEAM_APP_ID);
    assertStrictEqual(uri, 'steam://run/2622000', 'getSteamLaunchUri(CANONICAL) === steam://run/2622000');
  });

  runTest('Check 4: getSteamLaunchUri(customAppId) returns steam://run/${customAppId}', () => {
    const uri = getSteamLaunchUri(480);
    assertStrictEqual(uri, 'steam://run/480', 'getSteamLaunchUri(480) === steam://run/480');
  });

  runTest('Check 4: userOwnedSpectatorAccountService.getLaunchUri resolves 2622000 for STEAM_CN and STEAM_GLOBAL', () => {
    const service = new UserOwnedSpectatorAccountService();
    assertStrictEqual(service.getLaunchUri('STEAM_CN'), 'steam://run/2622000', 'STEAM_CN launch URI');
    assertStrictEqual(service.getLaunchUri('STEAM_GLOBAL'), 'steam://run/2622000', 'STEAM_GLOBAL launch URI');
    assertStrictEqual(service.getLaunchUri('TAPTAP'), null, 'TAPTAP fails closed with null (no fallback to Steam)');
  });

  // ---------------------------------------------------------------------------
  // Check 5: Scan Production Source Files Verifying ZERO Occurrences of 2514700
  // ---------------------------------------------------------------------------
  console.log('\n--- Check 5: Source Code Audit for Prohibited App ID 2514700 ---');

  // @ts-ignore
  const currentDir = path.dirname(fileURLToPath(import.meta.url));
  const repoRoot = path.resolve(currentDir, '../../..');

  const targetProductionFiles = [
    'AstralParty-Hub/src-tauri/src/xing_shu/platform/platform_profiles.json',
    'AstralParty-Hub/src-tauri/src/xing_shu/platform/profile.rs',
    'AstralParty-Hub/src/types/platform.ts',
    'AstralParty-Hub/src/types/spectatorAccount.ts',
    'AstralParty-Hub/src/services/spectatorAccountService.ts',
    'XingShu-Dev/src/XingShu.Runtime/XingShu/Platform/PlatformProfile.cs',
    'XingShu-Dev/src/XingShu.Runtime/XingShu/Platform/PlatformProfiles.Generated.cs',
    'XingShu-Dev/tools/generate_platform_profiles_cs.py',
  ];

  for (const relPath of targetProductionFiles) {
    runTest(`Check 5: Target file ${relPath} contains zero occurrences of 2514700`, () => {
      const fullPath = path.resolve(repoRoot, relPath);
      assert(fs.existsSync(fullPath), `Target file exists: ${relPath}`);
      const content = fs.readFileSync(fullPath, 'utf8');
      assert(
        !content.includes('2514700'),
        `Zero occurrences of 2514700 in ${relPath}`,
        `Found prohibited 2514700 in ${relPath}`
      );
    });
  }

  // Scan all files in production source directories
  const productionDirectories = [
    path.resolve(repoRoot, 'AstralParty-Hub/src/services'),
    path.resolve(repoRoot, 'AstralParty-Hub/src/types'),
    path.resolve(repoRoot, 'AstralParty-Hub/src-tauri/src/xing_shu'),
    path.resolve(repoRoot, 'XingShu-Dev/src/XingShu.Runtime/XingShu'),
  ];

  function scanDirRecursive(dir: string, fileList: string[] = []): string[] {
    if (!fs.existsSync(dir)) return fileList;
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const entryPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        scanDirRecursive(entryPath, fileList);
      } else if (entry.isFile()) {
        fileList.push(entryPath);
      }
    }
    return fileList;
  }

  runTest('Check 5: All files across production directories have ZERO occurrences of 2514700', () => {
    let scannedFileCount = 0;
    for (const dir of productionDirectories) {
      const files = scanDirRecursive(dir);
      for (const filePath of files) {
        scannedFileCount++;
        const content = fs.readFileSync(filePath, 'utf8');
        const relative = path.relative(repoRoot, filePath);
        assert(
          !content.includes('2514700'),
          `Zero occurrences of 2514700 in ${relative}`,
          `Found prohibited 2514700 in ${relative}`
        );
      }
    }
    console.log(`[INFO] Successfully audited ${scannedFileCount} production source files.`);
  });

  // ---------------------------------------------------------------------------
  // Summary Report
  // ---------------------------------------------------------------------------
  console.log('\n================================================================================');
  console.log(`CANONICAL STEAM APP IDENTITY RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
  console.log('================================================================================');

  if (failCount > 0) {
    process.exit(1);
  }
}

main();
