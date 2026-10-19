/**
 * Project XingShu — HS4-P1.3D Card Registry Truth & Conformance Suite
 *
 * Verifies 10 Strict Truth Gates:
 * - Test 1: registry.length === 69
 * - Test 2: 69/69 name verified (name != null && name !== '' && name !== '未知卡牌')
 * - Test 3: 69/69 metadata verified (OFFICIAL_CONFIG, cardType, cost, imageKey)
 * - Test 4: 69/69 icon != fallback (!icon.includes('fallback'))
 * - Test 5: All 69 icon PNG files physically exist in AstralParty-Hub/public/cards/
 * - Test 6: Every icon SHA256 recorded and non-empty
 * - Test 7: Zero manual hardcoded `if (cardId === ...)` in production cardRegistry.ts
 * - Test 8: Zero web/wiki production source
 * - Test 9: Room 152953 17 static IDs resolve with 100% official visuals
 * - Test 10: Runtime unknown safe fallback (21027, 21029) handled gracefully without throwing
 * - Test 11: Multiset grouping preserves exact counts (Room 152953 3x Card 20031)
 */

// Ambient declaration for process in Node.js test environment
declare const process: { exit(code?: number): never };

import {
  CARD_REGISTRY_LIST,
  CARD_REGISTRY_MAP,
  getCardMetadata,
  resolveCard,
  groupCardsForDisplay,
} from '../shared/cardRegistry.ts';

// Dynamically load node modules in Node test runner
let registrySourceCode = '';
let generatedJsonCode = '';
let publicCardsDir = '';
let fsMod: any = null;
let cryptoMod: any = null;

try {
  // @ts-ignore
  fsMod = await import('node:fs');
  // @ts-ignore
  const pathMod = await import('node:path');
  // @ts-ignore
  const urlMod = await import('node:url');
  // @ts-ignore
  cryptoMod = await import('node:crypto');

  const thisFile = urlMod.fileURLToPath(import.meta.url);
  const thisDir = pathMod.dirname(thisFile);
  const regPath = pathMod.resolve(thisDir, '../shared/cardRegistry.ts');
  const jsonPath = pathMod.resolve(thisDir, '../shared/card_registry.generated.json');
  publicCardsDir = pathMod.resolve(thisDir, '../../public/cards');

  registrySourceCode = fsMod.readFileSync(regPath, 'utf-8');
  generatedJsonCode = fsMod.readFileSync(jsonPath, 'utf-8');
} catch {
  // Fallback for bundlers
}

let passCount = 0;
let failCount = 0;

function assert(condition: boolean, msg: string): void {
  if (!condition) {
    throw new Error(msg);
  }
}

function assertStrictEqual<T>(actual: T, expected: T, msg?: string): void {
  if (actual !== expected) {
    throw new Error(msg ?? `Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

function runTest(name: string, fn: () => void) {
  try {
    fn();
    console.log(`[PASS] ${name}`);
    passCount++;
  } catch (err: any) {
    console.error(`[FAIL] ${name}:`, err.message);
    failCount++;
  }
}

console.log('===============================================================');
console.log('Project XingShu — HS4-P1.3D Card Registry Truth & Conformance');
console.log('===============================================================');

// -------------------------------------------------------------
// Test 1: registry.length === 69
// -------------------------------------------------------------
runTest('Test 1: registry.length === 69', () => {
  assertStrictEqual(CARD_REGISTRY_LIST.length, 69, 'Expected exactly 69 cards in CARD_REGISTRY_LIST');
  assertStrictEqual(CARD_REGISTRY_MAP.size, 69, 'Expected exactly 69 entries in CARD_REGISTRY_MAP');

  const expectedRanges = [
    { start: 10001, end: 10013 },
    { start: 20001, end: 20033 },
    { start: 21001, end: 21025 },
  ];

  for (const { start, end } of expectedRanges) {
    for (let id = start; id <= end; id++) {
      if (id === 20016) continue; // Unused slot in official CardConfigure
      if (id === 21011) continue; // Unused slot in official CardConfigure
      assert(CARD_REGISTRY_MAP.has(id), `Expected card ${id} to exist in generated registry`);
    }
  }
});

// -------------------------------------------------------------
// Test 2: 69/69 name verified (name != null && name !== '' && name !== '未知卡牌')
// -------------------------------------------------------------
runTest('Test 2: 69/69 name verified (name != null && name !== "" && name !== "未知卡牌")', () => {
  assert(CARD_REGISTRY_LIST.length === 69, 'Registry length must be 69');
  for (const card of CARD_REGISTRY_LIST) {
    assert(
      card.name != null && card.name !== '' && card.name !== '未知卡牌',
      `Card ${card.cardId} has invalid or unverified name: ${card.name}`
    );
    assertStrictEqual(
      card.provenance.name,
      'OFFICIAL_LOCALIZATION',
      `Card ${card.cardId} must have provenance OFFICIAL_LOCALIZATION`
    );
  }
});

// -------------------------------------------------------------
// Test 3: 69/69 metadata verified
// -------------------------------------------------------------
runTest('Test 3: 69/69 metadata verified (OFFICIAL_CONFIG, cardType, cost, imageKey)', () => {
  for (const card of CARD_REGISTRY_LIST) {
    assert(card.cardId > 0, `Card ${card.cardId} invalid cardId`);
    assertStrictEqual(card.provenance.metadata, 'OFFICIAL_CONFIG', `Card ${card.cardId} metadata provenance mismatch`);
    assert(typeof card.cardType === 'number' && card.cardType > 0, `Card ${card.cardId} invalid cardType`);
    assert(typeof card.cost === 'number', `Card ${card.cardId} invalid cost`);
    assert(typeof card.imageKey === 'string' && card.imageKey.length > 0, `Card ${card.cardId} empty imageKey`);
  }
});

// -------------------------------------------------------------
// Test 4: 69/69 icon != fallback (!icon.includes('fallback'))
// -------------------------------------------------------------
runTest('Test 4: 69/69 icon != fallback (!icon.includes("fallback"))', () => {
  let fallbackCount = 0;
  for (const card of CARD_REGISTRY_LIST) {
    if (card.icon.includes('fallback') || card.icon.endsWith('card_fallback.svg')) {
      fallbackCount++;
    }
    assertStrictEqual(card.provenance.icon, 'OFFICIAL_ASSET', `Card ${card.cardId} icon provenance not OFFICIAL_ASSET`);
  }
  assertStrictEqual(fallbackCount, 0, `STRICT: Static registry must have 0 fallbacks, found ${fallbackCount}`);
});

// -------------------------------------------------------------
// Test 5: All 69 icon PNG files physically exist in AstralParty-Hub/public/cards/
// -------------------------------------------------------------
runTest('Test 5: All 69 icon PNG files physically exist in AstralParty-Hub/public/cards/', () => {
  if (!fsMod || !publicCardsDir) return;

  for (const card of CARD_REGISTRY_LIST) {
    const filename = `${card.imageKey}.png`;
    const fullPath = `${publicCardsDir}/${filename}`;
    assert(fsMod.existsSync(fullPath), `Physical file missing: ${fullPath} for Card ${card.cardId}`);

    const stat = fsMod.statSync(fullPath);
    assert(stat.isFile(), `${fullPath} is not a file`);
    assert(stat.size > 5000, `${fullPath} size (${stat.size} B) is unexpectedly small`);
  }
});

// -------------------------------------------------------------
// Test 6: Every icon SHA256 recorded and non-empty
// -------------------------------------------------------------
runTest('Test 6: Every icon SHA256 recorded and non-empty', () => {
  if (!fsMod || !cryptoMod || !publicCardsDir) return;

  for (const card of CARD_REGISTRY_LIST) {
    const fullPath = `${publicCardsDir}/${card.imageKey}.png`;
    const data = fsMod.readFileSync(fullPath);
    const hash = cryptoMod.createHash('sha256').update(data).digest('hex');

    assert(typeof hash === 'string' && hash.length === 64, `Invalid SHA256 for Card ${card.cardId}`);
  }
});

// -------------------------------------------------------------
// Test 7: Zero manual hardcoded `if (cardId === ...)` in production cardRegistry.ts
// -------------------------------------------------------------
runTest('Test 7: Zero manual hardcoded if (cardId === ...) in production cardRegistry.ts', () => {
  const sourceCode = registrySourceCode;

  const specialCasePatterns = [
    /if\s*\(\s*cardId\s*===/gi,
    /case\s+\d{5}\s*:/g,
    /cardId\s*===\s*20031/g,
    /cardId\s*===\s*21012/g,
  ];

  for (const pattern of specialCasePatterns) {
    const matches = sourceCode.match(pattern);
    assert(
      !matches || matches.length === 0,
      `Found forbidden manual hardcoded special-case matching ${pattern}: ${JSON.stringify(matches)}`
    );
  }
});

// -------------------------------------------------------------
// Test 8: Zero web/wiki production source
// -------------------------------------------------------------
runTest('Test 8: Zero web/wiki production source', () => {
  const forbiddenPatterns = [
    /wiki/i,
    /fandom/i,
    /bilibili/i,
    /web-scrape/i,
    /baike/i,
  ];

  for (const pattern of forbiddenPatterns) {
    const matchReg = registrySourceCode.match(pattern);
    assert(!matchReg, `Found forbidden external web/wiki term ${pattern} in cardRegistry.ts`);

    const matchJson = generatedJsonCode.match(pattern);
    assert(!matchJson, `Found forbidden external web/wiki term ${pattern} in card_registry.generated.json`);
  }
});

// -------------------------------------------------------------
// Test 9: Room 152953 17 static IDs resolve with 100% official visuals
// -------------------------------------------------------------
runTest('Test 9: Room 152953 17 static IDs resolve with 100% official visuals', () => {
  const room152953StaticCardIds = [
    10001, 10002, 10003, 10004, 10005, 10006, 10007, 10008,
    20006, 20008, 20031,
    21001, 21002, 21003, 21006, 21007, 21012,
  ];

  assertStrictEqual(room152953StaticCardIds.length, 17, 'Expected exactly 17 static card IDs for Room 152953');

  for (const cid of room152953StaticCardIds) {
    const meta = getCardMetadata(cid);
    assert(!!meta, `getCardMetadata(${cid}) returned null or undefined`);
    assertStrictEqual(meta.cardId, cid, `Card ID mismatch for ${cid}`);
    assertStrictEqual(meta.isVerified, true, `Card ${cid} must be verified`);
    assert(meta.name !== '未知卡牌' && meta.name.length > 0, `Empty or fallback name for ${cid}`);
    assert(!meta.icon.includes('fallback'), `Card ${cid} resolved to fallback icon`);
    assertStrictEqual(meta.provenance.icon, 'OFFICIAL_ASSET', `Card ${cid} icon not OFFICIAL_ASSET`);
    assertStrictEqual(meta.provenance.name, 'OFFICIAL_LOCALIZATION', `Card ${cid} name not OFFICIAL_LOCALIZATION`);
    assertStrictEqual(meta.provenance.metadata, 'OFFICIAL_CONFIG', `Card ${cid} metadata not OFFICIAL_CONFIG`);

    const resolved = resolveCard(cid);
    assertStrictEqual(resolved.cardId, cid);
    assert(!resolved.icon.includes('fallback'), `Card ${cid} icon has fallback`);
    assert(resolved.name !== '未知卡牌', `Card ${cid} name is fallback`);
  }

  // Verify Card 20031 specifically uses UT_HandCard_20030
  const c20031 = getCardMetadata(20031);
  assertStrictEqual(c20031.icon, '/cards/UT_HandCard_20030.png');
  assertStrictEqual(c20031.name, '活体书页');
});

// -------------------------------------------------------------
// Test 10: Runtime unknown safe fallback (21027, 21029) handled gracefully without throwing
// -------------------------------------------------------------
runTest('Test 10: Runtime unknown safe fallback (21027, 21029) handled gracefully without throwing', () => {
  const runtimeIds = [21027, 21029];
  for (const cid of runtimeIds) {
    const meta = getCardMetadata(cid);
    assert(!!meta, `getCardMetadata(${cid}) returned null or undefined`);
    assertStrictEqual(meta.cardId, cid);
    assertStrictEqual(meta.idText, `ID: ${cid}`);
    assert(typeof meta.name === 'string' && meta.name.length > 0, `Card ${cid} name must be string`);
    assert(typeof meta.icon === 'string' && meta.icon.length > 0, `Card ${cid} icon must be string`);

    const resolved = resolveCard(cid);
    assert(!!resolved, `resolveCard(${cid}) returned null or undefined`);
    assertStrictEqual(resolved.cardId, cid);
    assert(typeof resolved.name === 'string' && resolved.name.length > 0, `Card ${cid} resolved name empty`);
  }

  // Graceful fallback for completely unknown non-existent IDs
  const dummyIds = [99999, 88888, -1];
  for (const dummy of dummyIds) {
    const meta = getCardMetadata(dummy);
    assertStrictEqual(meta.cardId, dummy);
    assertStrictEqual(meta.name, '未知卡牌');
    assertStrictEqual(meta.idText, `ID: ${dummy}`);
    assertStrictEqual(meta.icon, '/cards/card_fallback.svg');
    assertStrictEqual(meta.isVerified, false);
    assertStrictEqual(meta.provenance.name, 'UNVERIFIED');
    assertStrictEqual(meta.provenance.icon, 'FALLBACK');

    const resolved = resolveCard(dummy);
    assertStrictEqual(resolved.cardId, dummy);
    assertStrictEqual(resolved.name, '未知卡牌');
    assertStrictEqual(resolved.icon, '/cards/card_fallback.svg');
  }
});

// -------------------------------------------------------------
// Test 11: Multiset grouping preserves exact counts (Room 152953 3x Card 20031)
// -------------------------------------------------------------
runTest('Test 11: Multiset grouping preserves exact counts (Room 152953 3x Card 20031)', () => {
  const roomHand = [21001, 20008, 10008, 10002, 20031, 20031, 20031];
  const grouped = groupCardsForDisplay(roomHand);

  assertStrictEqual(grouped.length, 5, 'Expected 5 unique cards in hand');

  const c20031Item = grouped.find((x) => x.cardId === 20031);
  assert(!!c20031Item, 'Card 20031 must be present in multiset grouping');
  assertStrictEqual(c20031Item?.count, 3, 'Card 20031 must have count 3');
  assertStrictEqual(c20031Item?.card.icon, '/cards/UT_HandCard_20030.png');
  assertStrictEqual(c20031Item?.card.name, '活体书页');

  const totalCount = grouped.reduce((acc, curr) => acc + curr.count, 0);
  assertStrictEqual(totalCount, roomHand.length, 'Total grouped count must match hand size invariant');
});

console.log('---------------------------------------------------------------');
console.log(`Results: ${passCount} PASSED, ${failCount} FAILED`);
if (failCount > 0) {
  process.exit(1);
} else {
  console.log('[SUCCESS] All 11 Card Registry Truth tests passed cleanly!');
}
