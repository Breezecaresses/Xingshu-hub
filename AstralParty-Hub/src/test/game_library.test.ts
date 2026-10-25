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
}
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
