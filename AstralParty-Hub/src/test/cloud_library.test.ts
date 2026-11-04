import assert from 'node:assert/strict';
import {cloudMatchesAccount,mergeCloudLibrary} from '../shared/cloudLibrary.ts';
import type {LibraryAccount} from '../shared/gameLibrary.ts';
const local:LibraryAccount={platform:'STEAM_CN',role:'MAIN',observedAt:'2026-11-02T00:00:00Z',snapshot:{playerId:123,name:'旧资料',level:1,exp:0,heroes:[],skins:[],altArts:[],matches:[
  {time:10,heroId:102,mapId:2,rank:1,index:0,replayId:'10',version:'v',detailAvailable:true},
  {time:5,heroId:103,mapId:2,rank:1,index:1,replayId:'5',version:'v',detailAvailable:true}]}};
const cloud:LibraryAccount={...local,platform:'PHONE_CN',role:'CLOUD',source:'official_account',observedAt:'2026-11-03T00:00:00Z',snapshot:{...local.snapshot,name:'新资料',level:34,matches:[
  {time:20,heroId:104,mapId:2,rank:1,index:0,replayId:'20',version:'v',detailAvailable:true},
  {time:10,heroId:102,mapId:2,rank:1,index:1,replayId:'10',version:'v',detailAvailable:false}]}};
const result=mergeCloudLibrary([local],cloud);
assert.equal(result.length,1,'Same UID must not require a second phone-cloud account selection');
assert.equal(result[0].platform,'STEAM_CN');assert.equal(result[0].role,'MAIN','Manual binding stays local');
assert.equal(result[0].snapshot.name,'新资料');assert.equal(result[0].snapshot.level,34);
assert.deepEqual(result[0].snapshot.matches?.map(m=>m.time),[20,10,5]);
assert(result[0].snapshot.matches?.find(m=>m.time===10)?.detailAvailable,'Do not discard previously saved local details');
assert.equal(cloudMatchesAccount('123',local),true);
assert.equal(cloudMatchesAccount('456',local),false);
assert.equal(cloudMatchesAccount('123',{...local,platform:'STEAM_GLOBAL'}),false,'CN and global UID namespaces must remain separate');
assert.equal(mergeCloudLibrary([{...local,snapshot:{...local.snapshot,playerId:456}}],cloud).length,2);
assert.deepEqual(local.snapshot.matches?.map(m=>m.time),[10,5],'Merging does not mutate or delete local history');
console.log('PASS: same-UID cloud routing, CN isolation, progress freshness, local-role and older-history preservation');
