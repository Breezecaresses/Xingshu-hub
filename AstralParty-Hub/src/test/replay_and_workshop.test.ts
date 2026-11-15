import assert from 'node:assert/strict';
import { formatPercent, formatSignedInt, playerSeatColor } from '../pages/player/replay/replayShared.ts';
import { pickStaticEmoteAsset } from '../shared/gameLibrary.ts';
import { formatModBytes, installKindLabel, validateWorkshopSubmission } from '../shared/workshopClient.ts';

assert.equal(formatPercent(null), '无机会');
assert.equal(formatPercent(undefined), '无机会');
assert.equal(formatPercent(0), '0%');
assert.equal(formatPercent(67), '67%');
assert.equal(formatPercent(100), '100%');

assert.equal(formatSignedInt(152), '+152');
assert.equal(formatSignedInt(-40), '-40');
assert.equal(formatSignedInt(0), '0');
assert.equal(formatSignedInt(null), '—');

// Distinct 4-player seat colors for both 0-based legacy slots and 1-based slots
const zeroBasedColors = new Set([
  playerSeatColor({ slot: 0, label: 'P1' }),
  playerSeatColor({ slot: 1, label: 'P2' }),
  playerSeatColor({ slot: 2, label: 'P3' }),
  playerSeatColor({ slot: 3, label: 'P4' }),
]);
assert.equal(zeroBasedColors.size, 4);
assert.equal(playerSeatColor({ slot: 2, label: 'P2' }), '#3b82f6');
assert.equal(playerSeatColor({ slot: 4, label: 'P4' }), '#10b981');

assert.ok(pickStaticEmoteAsset(130, 'roster'), 'Hero 130 (苍鲤) must have a static emote fallback');
assert.equal(pickStaticEmoteAsset(307, 'roster'), '');
assert.equal(pickStaticEmoteAsset(308, 'roster'), '');

assert.equal(formatModBytes(512), '512 B');
assert.equal(formatModBytes(2048), '2.0 KB');
assert.equal(formatModBytes(3 * 1024 * 1024), '3.0 MB');
assert.equal(installKindLabel('xspkg'), '.xspkg 扩展包');
assert.equal(installKindLabel('bundle_mod'), 'Bundle 替换');

const validExternal = validateWorkshopSubmission({
  name: '优化卡面包',
  author: 'Breeze',
  category: 'card',
  description: '清晰化 3.3.0 部分手牌图标与边框。',
  tags: '卡牌替换,清晰化',
  targetInfo: 'UT_Card_1001',
  externalUrl: 'https://github.com/Breezecaresses/AstralParty-XingShu/releases',
});
assert.equal(validExternal, null);

assert.ok(
  validateWorkshopSubmission({
    name: 'A',
    author: 'Breeze',
    category: 'skin',
    description: 'short name fails',
    tags: '',
    targetInfo: '',
    externalUrl: 'https://example.com/mod.zip',
  }),
);

assert.ok(
  validateWorkshopSubmission({
    name: '不安全 HTTP 外链',
    author: 'Breeze',
    category: 'skin',
    description: 'http external link must be rejected',
    tags: '',
    targetInfo: '',
    externalUrl: 'http://example.com/mod.zip',
  }),
);

assert.ok(
  validateWorkshopSubmission({
    name: '缺少压缩包与外链',
    author: 'Breeze',
    category: 'optimization',
    description: 'missing both file and url',
    tags: '',
    targetInfo: '',
    externalUrl: '',
  }),
);
