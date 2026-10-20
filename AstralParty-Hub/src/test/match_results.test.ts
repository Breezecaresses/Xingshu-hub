import assert from 'node:assert/strict';
import { gameCatalog } from '../shared/gameLibrary.ts';
import { matchHistorySummary, matchResult } from '../shared/matchResults.ts';

const modes = gameCatalog.modes;
for (const mode of modes) {
  if (mode.group === 1) {
    assert.equal(matchResult(1, mode.id, modes).label, '胜利');
    assert.equal(matchResult(2, mode.id, modes).label, '失败');
    assert.equal(matchResult(2, mode.id, modes).kind, 'pve');
  } else if (mode.group === 2) {
    assert.equal(matchResult(1, mode.id, modes).label, '#1');
    assert.equal(matchResult(3, mode.id, modes).label, '#3');
    assert.equal(matchResult(3, mode.id, modes).kind, 'pvp');
  }
}
const mixed = [{mapId:4,rank:1},{mapId:4,rank:2},{mapId:1,rank:1},{mapId:1,rank:3},{mapId:999,rank:1},{mapId:1,rank:0}];
assert.deepEqual(matchHistorySummary(mixed,modes), {wins:2,decidedMatches:4,pvpMatches:2,winRate:50,averagePvpRank:2});
assert.deepEqual(matchHistorySummary([{mapId:4,rank:1},{mapId:4,rank:1}],modes), {wins:2,decidedMatches:2,pvpMatches:0,winRate:100,averagePvpRank:null});
assert.deepEqual(matchHistorySummary([],modes), {wins:0,decidedMatches:0,pvpMatches:0,winRate:null,averagePvpRank:null});
assert.equal(matchResult(1,999,modes).outcome,'unknown');
for (const rank of [0,-1,NaN,Infinity,1.5]) assert.equal(matchResult(rank,4,modes).outcome,'unknown');
console.log('Match results: cooperative victories/defeats, competitive ranks, mixed win rate and PVP-only average passed.');
