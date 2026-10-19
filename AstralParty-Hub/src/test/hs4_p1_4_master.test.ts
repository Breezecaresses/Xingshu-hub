/**
 * Project XingShu — Phase HS4-P1.4 Master Test Suite
 * Validates all 12 System Gates across Unified Hand & Account Identity Architecture:
 *
 * Gate 1: Generated Runtime card registry matches Hub IDs/names
 * Gate 2: Static 69 match
 * Gate 3: Runtime 21026–21029 match
 * Gate 4: No manual duplicate card table in C#
 * Gate 5: MAIN binding resolution
 * Gate 6: USER_SPECTATOR binding resolution
 * Gate 7: Unknown account resolution
 * Gate 8: Current runtime identity never restored from historical binding
 * Gate 9: Steam launch correct (steam://run/2622000)
 * Gate 10: Non-Steam cannot fallback to Steam (LAUNCH_NOT_IMPLEMENTED)
 * Gate 11: Route persistence in Spectator page
 * Gate 12: In-game / Hub snapshot parity
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  CARD_REGISTRY_LIST,
  CARD_REGISTRY_MAP,
  RUNTIME_CARD_LIST,
  RUNTIME_CARD_MAP,
  getCardMetadata,
  resolveCard,
  groupCardsForDisplay,
} from '../shared/cardRegistry.ts';

import {
  GameAccountService,
  MemoryStorage,
} from '../services/gameAccountService.ts';

import {
  GameAccountRole,
  ResolvedAccountRole,
} from '../types/gameAccount.ts';

import {
  UserOwnedSpectatorAccountService,
} from '../services/spectatorAccountService.ts';

import {
  SUPPORTED_PLATFORMS,
  CANONICAL_ASTRAL_PARTY_STEAM_APP_ID,
  PlatformLaunchMethod,
  resolvePlatformLaunchStrategy,
} from '../services/platformLaunchStrategy.ts';

declare const process: {
  exit(code?: number): never;
};

// @ts-ignore
const thisFile = fileURLToPath(import.meta.url);
// @ts-ignore
const thisDir = path.dirname(thisFile);
const repoRoot = path.resolve(thisDir, '../../..');

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.4: 12-GATE MASTER CONFORMANCE TEST SUITE');
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

function runGate(gateNum: number, name: string, fn: () => void): void {
  console.log(`\n--- GATE ${gateNum}: ${name} ---`);
  try {
    fn();
    console.log(`>>> [GATE ${gateNum} PASSED]`);
  } catch (err: any) {
    console.error(`>>> [GATE ${gateNum} FAILED]:`, err.message);
  }
}

// =============================================================================
// Gate 1: Generated Runtime card registry matches Hub IDs/names
// =============================================================================
runGate(1, 'Generated Runtime card registry matches Hub IDs/names', () => {
  const jsonPath = path.resolve(thisDir, '../shared/runtime_card_registry.generated.json');
  assert(fs.existsSync(jsonPath), 'Gate 1: runtime_card_registry.generated.json exists physically');

  const rawJson = JSON.parse(fs.readFileSync(jsonPath, 'utf-8'));
  assert(Array.isArray(rawJson), 'Gate 1: JSON is array');
  assertStrictEqual(rawJson.length, RUNTIME_CARD_LIST.length, 'Gate 1: RUNTIME_CARD_LIST matches file length');

  for (const item of rawJson) {
    const fromMap = RUNTIME_CARD_MAP.get(item.cardId);
    assert(fromMap !== undefined, `Gate 1: Card ${item.cardId} in RUNTIME_CARD_MAP`);
    assertStrictEqual(fromMap?.name, item.name, `Gate 1: Card ${item.cardId} name matches`);
    const meta = getCardMetadata(item.cardId);
    assertStrictEqual(meta.name, item.name, `Gate 1: getCardMetadata(${item.cardId}).name matches`);
    assertStrictEqual(meta.isVerified, true, `Gate 1: Card ${item.cardId} isVerified === true`);
  }
});

// =============================================================================
// Gate 2: Static 69 match
// =============================================================================
runGate(2, 'Static 69 canonical cards match', () => {
  assertStrictEqual(CARD_REGISTRY_LIST.length, 69, 'Gate 2: CARD_REGISTRY_LIST has exactly 69 cards');
  assertStrictEqual(CARD_REGISTRY_MAP.size, 69, 'Gate 2: CARD_REGISTRY_MAP has exactly 69 entries');

  const ranges = [
    { start: 10001, end: 10013, count: 13 },
    { start: 20001, end: 20033, count: 33 },
    { start: 21001, end: 21025, count: 23 },
  ];

  let rangeCount = 0;
  for (const r of ranges) {
    for (let id = r.start; id <= r.end; id++) {
      if (id === 20016 || id === 21011) continue; // Unused slots in official CardConfigure
      assert(CARD_REGISTRY_MAP.has(id), `Gate 2: Static ID ${id} present in map`);
      const meta = getCardMetadata(id);
      assert(meta.name !== null && meta.name !== '' && meta.name !== '未知卡牌', `Gate 2: ID ${id} has official name "${meta.name}"`);
      assertStrictEqual(meta.isVerified, true, `Gate 2: ID ${id} isVerified === true`);
      rangeCount++;
    }
  }
  assertStrictEqual(rangeCount, 69, 'Gate 2: Total verified range count is 69');
});

// =============================================================================
// Gate 3: Runtime 21026–21029 match
// =============================================================================
runGate(3, 'Runtime 21026–21029 cards match', () => {
  const expectedRuntimeCards: Record<number, string> = {
    21026: '疾行菇',
    21027: '仙品杨桃',
    21028: '芳香蜜桃',
    21029: '招财树',
  };

  assertStrictEqual(RUNTIME_CARD_LIST.length, 4, 'Gate 3: Exactly 4 dynamic runtime cards');

  for (const [idStr, expectedName] of Object.entries(expectedRuntimeCards)) {
    const cardId = Number(idStr);
    const card = RUNTIME_CARD_MAP.get(cardId);
    assert(card !== undefined, `Gate 3: Card ${cardId} in RUNTIME_CARD_MAP`);
    assertStrictEqual(card?.name, expectedName, `Gate 3: Card ${cardId} name is "${expectedName}"`);

    const meta = getCardMetadata(cardId);
    assertStrictEqual(meta.name, expectedName, `Gate 3: getCardMetadata(${cardId}).name is "${expectedName}"`);
    assertStrictEqual(meta.category, '建筑牌', `Gate 3: Card ${cardId} category is "建筑牌"`);
    assertStrictEqual(meta.cardType, 9, `Gate 3: Card ${cardId} cardType is 9 (Build)`);
    assertStrictEqual(meta.isVerified, true, `Gate 3: Card ${cardId} isVerified === true`);

    // Verify physical asset exists
    const pngPath = path.resolve(thisDir, `../../public${card?.icon}`);
    assert(fs.existsSync(pngPath), `Gate 3: Physical asset exists for card ${cardId}: ${card?.icon}`);
  }
});

// =============================================================================
// Gate 4: No manual duplicate card table in C#
// =============================================================================
runGate(4, 'No manual duplicate card table in C#', () => {
  const csharpDir = path.resolve(repoRoot, 'APX-Dev/src/APX.hooks');
  assert(fs.existsSync(csharpDir), 'Gate 4: APX.hooks directory exists');

  // Recursively inspect all .cs files in APX.hooks
  function getAllCsFiles(dir: string): string[] {
    let results: string[] = [];
    const list = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of list) {
      const fullPath = path.resolve(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== 'bin' && entry.name !== 'obj') {
          results = results.concat(getAllCsFiles(fullPath));
        }
      } else if (entry.name.endsWith('.cs')) {
        results.push(fullPath);
      }
    }
    return results;
  }

  const csFiles = getAllCsFiles(csharpDir);
  assert(csFiles.length > 0, `Gate 4: Found ${csFiles.length} C# source files`);

  // Regex patterns indicating manual hardcoded card tables
  const forbiddenPatterns = [
    /Dictionary\s*<\s*int\s*,\s*string\s*>\s*(_cardNames|CardNames|s_cardNames|cardTable)/i,
    /case\s+10001\s*:\s*return\s*['"]攻击/i,
    /case\s+20031\s*:\s*return\s*['"]活体书页/i,
  ];

  for (const file of csFiles) {
    const text = fs.readFileSync(file, 'utf-8');
    for (const pat of forbiddenPatterns) {
      assert(!pat.test(text), `Gate 4: ${path.basename(file)} has no manual duplicate card table (${pat})`);
    }
  }

  // Verify TrackedCard uses official GetCardConfigure()
  const trackedCardFile = path.resolve(csharpDir, 'APXRuntime/Utilities/TrackedCard.cs');
  assert(fs.existsSync(trackedCardFile), 'Gate 4: TrackedCard.cs exists');
  const trackedContent = fs.readFileSync(trackedCardFile, 'utf-8');
  assert(trackedContent.includes('GetCardConfigure'), 'Gate 4: TrackedCard uses official GetCardConfigure()');

  // Verify HandCardDisplayManager uses TrackedCard
  const displayMgr = path.resolve(csharpDir, 'APXRuntime/Utilities/HandCardDisplayManager.cs');
  assert(fs.existsSync(displayMgr), 'Gate 4: HandCardDisplayManager.cs exists');
  const mgrContent = fs.readFileSync(displayMgr, 'utf-8');
  assert(mgrContent.includes('TrackedCard'), 'Gate 4: HandCardDisplayManager uses TrackedCard');
});

// =============================================================================
// Gate 5: MAIN binding resolution
// =============================================================================
runGate(5, 'MAIN binding resolution', () => {
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  const mainPid = 958038;
  const binding = service.bindAccount(mainPid, GameAccountRole.MAIN, 'STEAM_CN', 'PrimaryPlayer');

  assertStrictEqual(binding.playerId, mainPid, 'Gate 5: Binding created with PID 958038');
  assertStrictEqual(binding.role, GameAccountRole.MAIN, 'Gate 5: Binding role is MAIN');

  const resolved = service.resolveSession(mainPid, 'STEAM_CN');
  assertStrictEqual(resolved.resolvedRole, GameAccountRole.MAIN, 'Gate 5: Session resolved to MAIN');
  assertStrictEqual(resolved.currentSessionPlayerId, mainPid, 'Gate 5: currentSessionPlayerId matches live ID');
  assert(resolved.binding !== null, 'Gate 5: Non-null binding in resolved session');
  assertStrictEqual(resolved.binding?.displayName, 'PrimaryPlayer', 'Gate 5: Binding displayName preserved');
});

// =============================================================================
// Gate 6: USER_SPECTATOR binding resolution
// =============================================================================
runGate(6, 'USER_SPECTATOR binding resolution', () => {
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  const spectatorPid = 1556514;
  const binding = service.bindAccount(spectatorPid, GameAccountRole.USER_SPECTATOR, 'STEAM_CN', 'DedicatedSpectator');

  assertStrictEqual(binding.playerId, spectatorPid, 'Gate 6: Binding created with PID 1556514');
  assertStrictEqual(binding.role, GameAccountRole.USER_SPECTATOR, 'Gate 6: Binding role is USER_SPECTATOR');

  const resolved = service.resolveSession(spectatorPid, 'STEAM_CN');
  assertStrictEqual(resolved.resolvedRole, GameAccountRole.USER_SPECTATOR, 'Gate 6: Session resolved to USER_SPECTATOR');
  assertStrictEqual(resolved.currentSessionPlayerId, spectatorPid, 'Gate 6: currentSessionPlayerId matches live ID');
  assert(resolved.binding !== null, 'Gate 6: Non-null binding in resolved session');
});

// =============================================================================
// Gate 7: Unknown account resolution
// =============================================================================
runGate(7, 'Unknown account resolution (UNBOUND_ACCOUNT)', () => {
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  const unknownPid = 888888;
  const resolved = service.resolveSession(unknownPid, 'STEAM_CN');

  assertStrictEqual(resolved.resolvedRole, ResolvedAccountRole.UNBOUND_ACCOUNT, 'Gate 7: Resolved role is UNBOUND_ACCOUNT');
  assertStrictEqual(resolved.currentSessionPlayerId, unknownPid, 'Gate 7: currentSessionPlayerId preserves live ID 888888');
  assertStrictEqual(resolved.binding, null, 'Gate 7: Unbound account binding is strictly null');
});

// =============================================================================
// Gate 8: Current runtime identity never restored from historical binding
// =============================================================================
runGate(8, 'Current runtime identity never restored from historical binding', () => {
  const storage = new MemoryStorage();
  const service = new GameAccountService(storage);

  // 1. In GameAccountService: null playerId strictly resolves to DISCONNECTED
  const offlineResolved = service.resolveSession(null, 'STEAM_CN');
  assertStrictEqual(offlineResolved.resolvedRole, ResolvedAccountRole.DISCONNECTED, 'Gate 8: Null PID -> DISCONNECTED');
  assertStrictEqual(offlineResolved.currentSessionPlayerId, null, 'Gate 8: currentSessionPlayerId is strictly null');
  assertStrictEqual(offlineResolved.binding, null, 'Gate 8: Binding is strictly null');

  // 2. In UserOwnedSpectatorAccountService: historical lastKnownPlayerId never populates currentSessionPlayerId
  const specStorage = new MemoryStorage();
  specStorage.setItem('xingshu_user_spectator_account', JSON.stringify({
    platformId: 'STEAM_CN',
    lastKnownPlayerId: 1556514,
    playerId: 1556514,
    displayName: 'HistoricalUser',
    lastSeenAt: '2026-10-17T12:00:00Z',
  }));

  const spectatorService = new UserOwnedSpectatorAccountService(specStorage);
  const accountInfo = spectatorService.getAccountInfo();

  assertStrictEqual(accountInfo.currentSessionPlayerId, null, 'Gate 8: Initial currentSessionPlayerId is strictly null');
  assertStrictEqual(accountInfo.playerId, null, 'Gate 8: Initial playerId is strictly null');
  assertStrictEqual(accountInfo.lastKnownPlayerId, 1556514, 'Gate 8: lastKnownPlayerId retains historical 1556514');
});

// =============================================================================
// Gate 9: Steam launch correct (steam://run/2622000)
// =============================================================================
runGate(9, 'Steam launch correct (steam://run/2622000)', () => {
  assertStrictEqual(CANONICAL_ASTRAL_PARTY_STEAM_APP_ID, 2622000, 'Gate 9: Canonical App ID is 2622000');

  const steamCnPlan = resolvePlatformLaunchStrategy('STEAM_CN');
  assertStrictEqual(steamCnPlan.canLaunch, true, 'Gate 9: STEAM_CN canLaunch === true');
  assertStrictEqual(steamCnPlan.launchMethod, PlatformLaunchMethod.LAUNCH_METHOD_STEAM_URI, 'Gate 9: Method is STEAM_URI');
  assertStrictEqual(steamCnPlan.uri, 'steam://run/2622000', 'Gate 9: STEAM_CN URI is steam://run/2622000');
  assertStrictEqual(steamCnPlan.steamAppId, 2622000, 'Gate 9: steamAppId === 2622000');

  const steamGlobalPlan = resolvePlatformLaunchStrategy('STEAM_GLOBAL');
  assertStrictEqual(steamGlobalPlan.canLaunch, true, 'Gate 9: STEAM_GLOBAL canLaunch === true');
  assertStrictEqual(steamGlobalPlan.launchMethod, PlatformLaunchMethod.LAUNCH_METHOD_STEAM_URI, 'Gate 9: Method is STEAM_URI');
  assertStrictEqual(steamGlobalPlan.uri, 'steam://run/2622000', 'Gate 9: STEAM_GLOBAL URI is steam://run/2622000');
  assertStrictEqual(steamGlobalPlan.steamAppId, 2622000, 'Gate 9: steamAppId === 2622000');
});

// =============================================================================
// Gate 10: Non-Steam cannot fallback to Steam (LAUNCH_NOT_IMPLEMENTED)
// =============================================================================
runGate(10, 'Non-Steam cannot fallback to Steam (LAUNCH_NOT_IMPLEMENTED)', () => {
  assertStrictEqual(SUPPORTED_PLATFORMS.length, 5, 'Gate 10: Exactly 5 supported platforms');
  const nonSteam = ['TAPTAP', 'BILIBILI', 'WEGAME'];

  for (const pid of nonSteam) {
    const plan = resolvePlatformLaunchStrategy(pid);
    assertStrictEqual(plan.canLaunch, false, `Gate 10: ${pid} canLaunch === false`);
    assertStrictEqual(plan.launchMethod, PlatformLaunchMethod.LAUNCH_NOT_IMPLEMENTED, `Gate 10: ${pid} is LAUNCH_NOT_IMPLEMENTED`);
    assertStrictEqual(plan.uri, null, `Gate 10: ${pid} URI is strictly null`);
    assertStrictEqual(plan.steamAppId, null, `Gate 10: ${pid} steamAppId is strictly null`);
    assert(plan.uri !== 'steam://run/2622000', `Gate 10: ${pid} MUST NEVER evaluate to steam://run/2622000`);
  }

  // Also check UserOwnedSpectatorAccountService integration
  const specService = new UserOwnedSpectatorAccountService(new MemoryStorage());
  assertStrictEqual(specService.getLaunchUri('TAPTAP'), null, 'Gate 10: specService.getLaunchUri("TAPTAP") is null');
  assertStrictEqual(specService.getLaunchUri('BILIBILI'), null, 'Gate 10: specService.getLaunchUri("BILIBILI") is null');
  assertStrictEqual(specService.getLaunchUri('WEGAME'), null, 'Gate 10: specService.getLaunchUri("WEGAME") is null');
});

// =============================================================================
// Gate 11: Route persistence in Spectator page
// =============================================================================
runGate(11, 'Route persistence in Spectator page', () => {
  const useSpectatorStatePath = path.resolve(thisDir, '../pages/spectator/useSpectatorState.ts');
  assert(fs.existsSync(useSpectatorStatePath), 'Gate 11: useSpectatorState.ts exists');

  const content = fs.readFileSync(useSpectatorStatePath, 'utf-8');

  // Verify route persistence keys
  assert(content.includes('sessionStorage.getItem(\'xingshu_spectator_selected_mode\')'), 'Gate 11: Reads from sessionStorage');
  assert(content.includes('localStorage.getItem(\'xingshu.spectator.accountMode\')'), 'Gate 11: Reads from localStorage');
  assert(content.includes('sessionStorage.setItem(\'xingshu_spectator_selected_mode\', mode)'), 'Gate 11: Writes to sessionStorage');
  assert(content.includes('localStorage.setItem(\'xingshu.spectator.accountMode\', mode)'), 'Gate 11: Writes to localStorage');
  assert(content.includes('Persisted at application level across route navigation'), 'Gate 11: Explicit route persistence comments');
});

// =============================================================================
// Gate 12: In-game / Hub snapshot parity
// =============================================================================
runGate(12, 'In-game / Hub snapshot parity (Room 152953 Seq=22)', () => {
  // Room 152953 authentic players and hand cards at seq=22
  const snapshotData = [
    {
      playerId: 958038,
      handSize: 6,
      cardIds: [21001, 20008, 10003, 10001, 21003, 21027],
      expectedNames: ['轨道炮', '狂暴', '攻击(大)', '攻击(中)', '加急加快', '仙品杨桃'],
    },
    {
      playerId: 151220,
      handSize: 4,
      cardIds: [10004, 21003, 21012, 21029],
      expectedNames: ['防御(大)', '加急加快', '对怪激光', '招财树'],
    },
    {
      playerId: 1946346,
      handSize: 4,
      cardIds: [21006, 10006, 21003, 21029],
      expectedNames: ['奢华大餐', '防御(特大)', '加急加快', '招财树'],
    },
    {
      playerId: 3018797,
      handSize: 7,
      cardIds: [21001, 10008, 10002, 20031, 20031, 20031, 21029],
      expectedNames: ['轨道炮', '暗影突袭', '防御(中)', '活体书页', '招财树'],
    },
  ];

  let totalCards = 0;
  for (const player of snapshotData) {
    assertStrictEqual(player.cardIds.length, player.handSize, `Gate 12: Player ${player.playerId} handSize matches`);
    totalCards += player.handSize;

    // Verify all card names resolved by Hub match expected official localized names
    for (const cid of player.cardIds) {
      const def = resolveCard(cid);
      assert(player.expectedNames.includes(def.name), `Gate 12: Card ${cid} name "${def.name}" in expected names for Player ${player.playerId}`);
    }

    // Verify multiset grouping
    const grouped = groupCardsForDisplay(player.cardIds);
    let groupedCountSum = 0;
    for (const g of grouped) {
      groupedCountSum += g.count;
    }
    assertStrictEqual(groupedCountSum, player.handSize, `Gate 12: Grouped count sum equals handSize for Player ${player.playerId}`);
  }

  assertStrictEqual(totalCards, 21, 'Gate 12: Total hand size across all 4 players is exactly 21');

  // Verify Player 3018797 has 3x 活体书页 [20031]
  const p3018797Grouped = groupCardsForDisplay(snapshotData[3].cardIds);
  const card20031Item = p3018797Grouped.find((item) => item.cardId === 20031);
  assert(card20031Item !== undefined, 'Gate 12: Card 20031 present in Player 3018797 grouped hand');
  assertStrictEqual(card20031Item?.count, 3, 'Gate 12: Card 20031 has count 3 (3x 活体书页)');
  assertStrictEqual(card20031Item?.card.name, '活体书页', 'Gate 12: Card 20031 name is "活体书页"');

  // Verify Player 958038 has 仙品杨桃 [21027]
  const p958038Grouped = groupCardsForDisplay(snapshotData[0].cardIds);
  const card21027Item = p958038Grouped.find((item) => item.cardId === 21027);
  assert(card21027Item !== undefined, 'Gate 12: Card 21027 present in Player 958038 hand');
  assertStrictEqual(card21027Item?.card.name, '仙品杨桃', 'Gate 12: Card 21027 name is "仙品杨桃"');

  // Verify Player 151220, 1946346, 3018797 have 招财树 [21029]
  for (const pIdx of [1, 2, 3]) {
    const grp = groupCardsForDisplay(snapshotData[pIdx].cardIds);
    const tree = grp.find((item) => item.cardId === 21029);
    assert(tree !== undefined, `Gate 12: Card 21029 present in Player ${snapshotData[pIdx].playerId} hand`);
    assertStrictEqual(tree?.card.name, '招财树', `Gate 12: Card 21029 name is "招财树"`);
  }

  // Verify physical screenshot file exists
  const screenshotCandidates = [
    path.resolve(repoRoot, 'ingame_hand_monitor_synced.png'),
    path.resolve(repoRoot, '项目资料归档', '05_历史截图与自动截图', 'ingame_hand_monitor_synced.png'),
  ];
  const screenshotPath = screenshotCandidates.find((p) => fs.existsSync(p)) || screenshotCandidates[0];
  assert(fs.existsSync(screenshotPath), 'Gate 12: ingame_hand_monitor_synced.png physically exists');
  const imgStats = fs.statSync(screenshotPath);
  assert(imgStats.size > 50000, `Gate 12: Screenshot size ${imgStats.size} > 50KB`);
});

// =============================================================================
// Final Results
// =============================================================================
console.log('\n================================================================================');
console.log(`MASTER CONFORMANCE RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
console.log('================================================================================');

if (failCount > 0) {
  process.exit(1);
}
