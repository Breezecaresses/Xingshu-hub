import assert from 'node:assert/strict';
import { rateMatch } from '../shared/matchRating.ts';
import type { MatchParticipant } from '../shared/gameLibrary.ts';
const p: MatchParticipant={playerId:1,name:'测试',heroId:101,rank:1,level:5,gold:10,slot:0,giveUp:false,playerLevel:20,kills:2,deaths:0,damage:100,taken:30,healing:20,coins:50,transfer:5,turns:10};
assert.deepEqual(rateMatch([{...p,damage:null},p]),[null,null]);
assert.deepEqual(rateMatch([{...p,kills:0,damage:0,healing:0,coins:0,transfer:0}]),[null]);
assert.deepEqual(rateMatch([p,p]),[10,10]);
const scores=rateMatch([p,{...p,deaths:3}]);
assert.ok(scores[0]!>scores[1]! && scores[1]!>0);
const scaled=[{...p,transfer:25},{...p,damage:50,healing:0,kills:1,transfer:30}];
assert.deepEqual(rateMatch(scaled),rateMatch(scaled.map(x=>({...x,damage:x.damage!*5,healing:x.healing!*5,kills:x.kills!*5,coins:x.coins!*5,transfer:x.transfer!*5}))));
assert.ok(rateMatch(scaled).every(v=>v!=null&&v>=0&&v<=10));
// A real cooperative settlement: funding players must not be judged only by damage.
const real=[
  {...p,kills:0,damage:11,healing:3,coins:73,transfer:50,gold:17},
  {...p,kills:0,damage:26,healing:13,coins:70,transfer:30,gold:4},
  {...p,kills:10,deaths:1,damage:314,healing:30,coins:153,transfer:0,gold:12},
  {...p,kills:4,damage:57,healing:0,coins:104,transfer:5,gold:16},
];
assert.deepEqual(rateMatch(real),[8.2,8.07,8.7,2.36]);
assert.deepEqual(rateMatch(real),rateMatch(real.map(x=>({...x,gold:999999}))));
const funding={...p,kills:0,damage:0,healing:0,coins:0,transfer:20};
assert.ok(rateMatch([funding,real[2]])[0]!>=6);
assert.ok(rateMatch([{...funding,deaths:2},real[2]])[0]!<rateMatch([funding,real[2]])[0]!);
assert.ok(rateMatch([{...funding,transfer:19.9},real[2]])[0]!<=rateMatch([funding,real[2]])[0]!);
assert.deepEqual(rateMatch([{...p,transfer:null},p]),[null,null]);
console.log('Match ratings v2: real support settlement, 20+ transfers, remaining gold independence, bounds, missing stats and death penalty passed.');
