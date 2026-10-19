/**
 * Project XingShu — HS4-P1.2.1 Identity Truth Test Suite
 * Eradication of Test Identity Leakage (1556514) & Fallback Truth Conformance
 *
 * Assertions:
 * 1. Runtime disconnected -> playerId is strictly null, UI text evaluates to '未识别'.
 * 2. Runtime connected but no packet received -> playerId is null, UI text evaluates to '等待 Runtime 识别'.
 * 3. Real payload received with spectatorAccountPlayerId: 1556514 -> UI displays '1556514'.
 * 4. Real payload received with arbitrary player ID 999999 -> UI displays '999999'.
 * 5. Static scan of AstralParty-Hub/src/pages/spectator/ verifying ZERO occurrences of 1556514 in production UI fallback expressions.
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  INITIAL_ROOM_STATE,
  processSpectatorTransition,
  formatSpectatorPlayerId,
  type SpectatorRoomState,
} from '../pages/spectator/spectatorTransition.ts';
import type { SpectatorHandPayload } from '../shared/types/index.ts';

declare const process: {
  exit(code?: number): never;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.2.1: IDENTITY TRUTH & ZERO-LEAKAGE CONFORMANCE SUITE');
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

// -----------------------------------------------------------------------------
// Test 1: Runtime disconnected -> playerId is strictly null, UI text evaluates to '未识别'
// -----------------------------------------------------------------------------
console.log('\n--- Test 1: Runtime Disconnected Identity Truth ---');

runTest('Test 1: Initial room state spectatorAccountPlayerId is strictly null', () => {
  assertStrictEqual(INITIAL_ROOM_STATE.spectatorAccountPlayerId, null, 'INITIAL_ROOM_STATE.spectatorAccountPlayerId === null');
});

runTest('Test 1: When disconnected, spectatorPlayerId is strictly null (no fallback)', () => {
  const isConnected = false;
  const roomState: SpectatorRoomState = { ...INITIAL_ROOM_STATE };
  const spectatorPlayerId = roomState.spectatorAccountPlayerId ?? null;
  assertStrictEqual(spectatorPlayerId, null, 'spectatorPlayerId is strictly null when disconnected');
  assertStrictEqual(formatSpectatorPlayerId(spectatorPlayerId, isConnected, true), '未识别', 'formatSpectatorPlayerId with null is 未识别');
});

runTest('Test 1: When disconnected and playerId is null, UI text evaluates to 未识别', () => {
  const isConnected = false;
  const textZh = formatSpectatorPlayerId(null, isConnected, true);
  assertStrictEqual(textZh, '未识别', 'formatSpectatorPlayerId(null, false, true) === "未识别"');

  const textEn = formatSpectatorPlayerId(null, isConnected, false);
  assertStrictEqual(textEn, 'Unidentified', 'formatSpectatorPlayerId(null, false, false) === "Unidentified"');
});

runTest('Test 1: When disconnected and playerId is undefined or 0, UI text evaluates to 未识别', () => {
  const isConnected = false;
  assertStrictEqual(formatSpectatorPlayerId(undefined, isConnected, true), '未识别', 'undefined playerId -> 未识别');
  assertStrictEqual(formatSpectatorPlayerId(0, isConnected, true), '未识别', '0 playerId -> 未识别');
});

// -----------------------------------------------------------------------------
// Test 2: Runtime connected but no packet received -> playerId is null, UI text evaluates to '等待 Runtime 识别'
// -----------------------------------------------------------------------------
console.log('\n--- Test 2: Runtime Connected (Pre-Packet) Identity Truth ---');

runTest('Test 2: When connected but no payload arrived, spectatorPlayerId is strictly null', () => {
  const isConnected = true;
  const roomState: SpectatorRoomState = { ...INITIAL_ROOM_STATE };
  const spectatorPlayerId = roomState.spectatorAccountPlayerId ?? null;
  assertStrictEqual(spectatorPlayerId, null, 'spectatorPlayerId is strictly null (NEVER defaults to 1556514)');
  assertStrictEqual(formatSpectatorPlayerId(spectatorPlayerId, isConnected, true), '等待 Runtime 识别', 'formatSpectatorPlayerId with null when connected is 等待 Runtime 识别');
});

runTest('Test 2: When connected and playerId is null, UI text evaluates to 等待 Runtime 识别', () => {
  const isConnected = true;
  const textZh = formatSpectatorPlayerId(null, isConnected, true);
  assertStrictEqual(textZh, '等待 Runtime 识别', 'formatSpectatorPlayerId(null, true, true) === "等待 Runtime 识别"');

  const textEn = formatSpectatorPlayerId(null, isConnected, false);
  assertStrictEqual(textEn, 'Waiting for Runtime', 'formatSpectatorPlayerId(null, true, false) === "Waiting for Runtime"');
});

runTest('Test 2: When connected and playerId is undefined or 0, UI text evaluates to 等待 Runtime 识别', () => {
  const isConnected = true;
  assertStrictEqual(formatSpectatorPlayerId(undefined, isConnected, true), '等待 Runtime 识别', 'undefined playerId -> 等待 Runtime 识别');
  assertStrictEqual(formatSpectatorPlayerId(0, isConnected, true), '等待 Runtime 识别', '0 playerId -> 等待 Runtime 识别');
});

// -----------------------------------------------------------------------------
// Test 3: Real payload received with spectatorAccountPlayerId: 1556514 -> UI displays '1556514'
// -----------------------------------------------------------------------------
console.log('\n--- Test 3: Authentic Payload with ID 1556514 Dynamically Recognized ---');

runTest('Test 3: Transition accepts payload with spectatorAccountPlayerId 1556514', () => {
  const payload1556514: SpectatorHandPayload = {
    schemaVersion: '1.0.0',
    roomId: 665945,
    spectatorSessionEpoch: 1,
    sequenceNumber: 1,
    playerId: 1556514,
    spectatorAccountPlayerId: 1556514,
    roomPlayers: {
      '1556514': { playerId: 1556514, handSize: 3, cardIds: [10001, 10002, 10003], cards: [] },
    },
  };

  const result = processSpectatorTransition(INITIAL_ROOM_STATE, payload1556514);
  assert(result.accepted, 'Transition accepted payload 1556514');
  assertStrictEqual(result.nextState.spectatorAccountPlayerId, 1556514, 'nextState captures 1556514 dynamically');

  // Verify UI displays exact player ID
  const isConnected = true;
  const displayText = formatSpectatorPlayerId(result.nextState.spectatorAccountPlayerId, isConnected, true);
  assertStrictEqual(displayText, '1556514', 'UI display evaluates to "1556514"');
});

// -----------------------------------------------------------------------------
// Test 4: Real payload received with another arbitrary player ID 999999 -> UI displays '999999'
// -----------------------------------------------------------------------------
console.log('\n--- Test 4: Dynamic Arbitrary Player ID Recognition ---');

runTest('Test 4: Transition accepts payload with arbitrary spectatorAccountPlayerId 999999', () => {
  const payload999999: SpectatorHandPayload = {
    schemaVersion: '1.0.0',
    roomId: 777123,
    spectatorSessionEpoch: 1,
    sequenceNumber: 1,
    playerId: 999999,
    spectatorAccountPlayerId: 999999,
    roomPlayers: {
      '999999': { playerId: 999999, handSize: 2, cardIds: [20001, 20002], cards: [] },
    },
  };

  const result = processSpectatorTransition(INITIAL_ROOM_STATE, payload999999);
  assert(result.accepted, 'Transition accepted payload 999999');
  assertStrictEqual(result.nextState.spectatorAccountPlayerId, 999999, 'nextState captures 999999 dynamically');

  // Verify UI displays exact player ID
  const isConnected = true;
  const displayText = formatSpectatorPlayerId(result.nextState.spectatorAccountPlayerId, isConnected, true);
  assertStrictEqual(displayText, '999999', 'UI display evaluates to "999999"');
});

runTest('Test 4: Additional arbitrary spectator player IDs format truthfully', () => {
  assertStrictEqual(formatSpectatorPlayerId(2088888, true, true), '2088888', 'Player ID 2088888 formatted');
  assertStrictEqual(formatSpectatorPlayerId(1234567, false, true), '1234567', 'Player ID 1234567 formatted');
  assertStrictEqual(formatSpectatorPlayerId(42, true, false), '42', 'Player ID 42 formatted in EN');
});

// -----------------------------------------------------------------------------
// Test 5: Static scan of AstralParty-Hub/src/pages/spectator/ verifying ZERO occurrences
// of 1556514 in production UI fallback expressions
// -----------------------------------------------------------------------------
console.log('\n--- Test 5: Static Codebase Audit for Production Identity Leakage ---');

runTest('Test 5: Zero occurrences of fallback expressions or hardcoded 1556514 in spectator pages', () => {
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);
  const spectatorPagesDir = path.resolve(__dirname, '../pages/spectator');

  assert(fs.existsSync(spectatorPagesDir), `Directory exists: ${spectatorPagesDir}`);

  function getFilesRecursively(dir: string): string[] {
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    const files: string[] = [];
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        files.push(...getFilesRecursively(fullPath));
      } else if (entry.isFile() && (entry.name.endsWith('.ts') || entry.name.endsWith('.tsx'))) {
        files.push(fullPath);
      }
    }
    return files;
  }

  const productionFiles = getFilesRecursively(spectatorPagesDir);
  assert(productionFiles.length > 0, `Found ${productionFiles.length} production spectator files to audit`);

  const forbiddenFallbackPatterns = [
    { pattern: /\?\?\s*['"]?1556514['"]?/, name: 'Nullish coalescing fallback (?? 1556514)' },
    { pattern: /\?\s*['"]?1556514['"]?\s*:/, name: 'Ternary truthy branch (? 1556514 :)' },
    { pattern: /:\s*['"]?1556514['"]?/, name: 'Ternary fallback branch (: 1556514)' },
    { pattern: /['"]1556514['"]/, name: 'String literal "1556514"' },
    { pattern: /\b1556514\b/, name: 'Numeric literal 1556514' },
  ];

  for (const filePath of productionFiles) {
    const relPath = path.relative(path.resolve(__dirname, '../../'), filePath);
    const content = fs.readFileSync(filePath, 'utf8');

    for (const { pattern, name } of forbiddenFallbackPatterns) {
      const match = pattern.exec(content);
      assert(
        match === null,
        `Clean scan [${relPath}]: Zero ${name}`,
        `Forbidden match found in ${relPath}: "${match?.[0]}"`
      );
    }
  }

  // Also verify spectatorAccount.ts comment doesn't imply 1556514 is a default
  const typesPath = path.resolve(__dirname, '../types/spectatorAccount.ts');
  const typesContent = fs.readFileSync(typesPath, 'utf8');
  assert(
    !typesContent.includes('e.g. 1556514'),
    'spectatorAccount.ts does not suggest 1556514 as default in comments'
  );
});

// -----------------------------------------------------------------------------
// Summary
// -----------------------------------------------------------------------------
console.log('\n================================================================================');
console.log(`IDENTITY TRUTH TEST RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
console.log('================================================================================');

if (failCount > 0) {
  process.exit(1);
}
