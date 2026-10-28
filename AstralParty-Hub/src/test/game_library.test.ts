import assert from 'node:assert/strict';
import { accountKey, defaultLibraryKey, gameCatalog, plainGameText, isPreviewCharacter, libraryCharacterName, matchDetailRefreshComplete } from '../shared/gameLibrary.ts';
import type { LibraryAccount, LibrarySession, MatchDetail } from '../shared/gameLibrary.ts';

const saved: MatchDetail = {requestId:'old',playerId:1,index:0,time:1,heroId:101,mapId:81003,status:'ready',error:'',participants:[],observedAt:'2026-10-24',statsSource:'replay_settlement',statsError:''};
assert.equal(matchDetailRefreshComplete(saved,'new'),false,'An older saved result must not end a refresh');
assert.equal(matchDetailRefreshComplete({...saved,requestId:'new',status:'loading'},'new'),false);
assert.equal(matchDetailRefreshComplete({...saved,requestId:'new'},'new'),true);
assert.equal(matchDetailRefreshComplete({...saved,requestId:'new',refreshError:'读取未完成'},'new'),true,'A failed refresh can preserve saved metrics');
assert.equal(matchDetailRefreshComplete({...saved,requestId:'new',status:'error'},'new'),true);
assert.equal(matchDetailRefreshComplete(null,'new'),false);

const spectator: LibraryAccount = {platform:'TAPTAP',role:'USER_SPECTATOR',observedAt:'2026-10-20',snapshot:{playerId:2,name:'观战',level:1,exp:0,heroes:[],skins:[],altArts:[],matches:null}};
const main: LibraryAccount = {...spectator,platform:'STEAM_CN',role:'MAIN',snapshot:{...spectator.snapshot,playerId:1}};
const liveMain: LibrarySession={connectionId:'c',platformId:'STEAM_CN',livePlayerId:3,boundRole:'MAIN',connectionState:'CONNECTED',runtimeVersion:'1'};
assert.equal(defaultLibraryKey([spectator],[],null),'');
assert.equal(defaultLibraryKey([spectator,main],[],null),accountKey(main));
assert.equal(defaultLibraryKey([spectator,main],[liveMain],null),'STEAM_CN:3');
assert.equal(defaultLibraryKey([spectator,main],[liveMain],accountKey(spectator)),accountKey(spectator));
assert.equal(defaultLibraryKey([main],[],'missing'),accountKey(main));
assert.equal(plainGameText('[color=#fff]攻击+1[/color]<b>效果</b>'),'攻击+1效果');
assert.equal(plainGameText('指定{range=3}格内，造成{value=5}点伤害'),'指定3格内，造成5点伤害');
assert.equal(gameCatalog.characters.length,37);
assert.deepEqual(gameCatalog.characters.filter(isPreviewCharacter).map(c=>c.id),[307,308]);
assert.equal(gameCatalog.characters.filter(c=>!isPreviewCharacter(c)).length,35);
for(const character of gameCatalog.characters.filter(isPreviewCharacter)){
  assert.equal(libraryCharacterName(character),`预更新角色 #${character.id}`);
  assert.ok(character.hp>0,'Preserve official provisional values');
  assert.equal(character.image,'','Borrowed released-character avatars must not identify preview characters');
  assert.equal(character.hasBond,false);
  assert.deepEqual(character.bondRewards,[],'Inactive shared bond rewards must not be displayed as preview rewards');
  assert.deepEqual(character.breakthrough,{});
  assert.ok(character.skins.every(s=>s.name==='初始装扮（名称待更新）'));
  assert.equal(character.preview?.skinSlots,1);
  assert.equal(character.preview?.emoteSlots,8);
  assert.equal(character.preview?.unavailableAssets.length,9);
  assert.ok(character.preview?.placeholderReferences.some(r=>r.field==='战斗资源'));
  assert.equal(gameCatalog.itemNames?.[String(character.id)],`预更新角色 #${character.id}`);
  for(const skin of character.skins) assert.equal(gameCatalog.itemNames?.[String(skin.id)],skin.name);
}
const preview307=gameCatalog.characters.find(c=>c.id===307)!;
const preview308=gameCatalog.characters.find(c=>c.id===308)!;
assert.deepEqual([preview307.hp,preview307.attack,preview307.defense],[7,1,0]);
assert.deepEqual([preview308.hp,preview308.attack,preview308.defense],[9,1,1]);
assert.deepEqual(preview307.skills.map(s=>s.id),[30701,30711,30712]);
assert.deepEqual(preview307.pveSkills.map(s=>s.id),[30702,30713,30714,30715]);
assert.deepEqual(preview308.skills.map(s=>s.id),[30801,30811]);
assert.deepEqual(preview308.pveSkills.map(s=>s.id),[30802,30812,30813]);
assert.deepEqual(preview307.skills[0].params,[6,-2,1]);
assert.deepEqual(preview307.pveSkills[0].params,[6,-3,1,3071202]);
assert.deepEqual([preview307.skills[0].cooldown,preview307.pveSkills[0].cooldown],[4,3]);
assert.deepEqual([preview308.skills[0].cooldown,preview308.pveSkills[0].cooldown],[3,3]);
assert.ok(preview307.skills.concat(preview307.pveSkills,preview308.skills,preview308.pveSkills).every(s=>!s.name&&!s.description),'Unreleased skill prose must not be fabricated from params');
const availableBuff=preview308.pveSkills.flatMap(s=>s.buffs||[]).find(b=>b.id===3081104)!;
assert.equal(availableBuff.description,'费用上限+3，移动速度+2，出牌数+1');
assert.deepEqual(preview308.pveSkills.find(s=>s.id===30813)?.relatedCharacterIds,[305,306,307,308]);
assert.equal(new Set(gameCatalog.characters.map(c=>c.id)).size,gameCatalog.characters.length);
assert.equal(gameCatalog.cards.length,73);
for(const card of gameCatalog.cards){
  assert.ok(card.name && card.description,`Card ${card.id}: official text missing`);
  assert.ok(gameCatalog.assets[card.image],`Card ${card.id}: image missing`);
}
for(const art of gameCatalog.altArts){
  assert.ok(gameCatalog.cards.some(c=>c.id===art.cardId));
  assert.ok(gameCatalog.assets[art.image]);
}
assert.equal(gameCatalog.favorLevels.length,7);
console.log('Game library: account isolation, unknown state, real catalog effects and images passed.');
