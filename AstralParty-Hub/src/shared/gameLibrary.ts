import rawCatalog from './gameCatalog.generated.json' with { type: 'json' };

export interface LibrarySkill { id: number; name: string; description: string; cooldown: number }
export interface LibrarySkin { id: number; index: number; default: boolean; name: string; description: string; image: string }
export interface LibraryCharacter {
  id: number; name: string; title: string; biography: string; comment: string; gallery: boolean;
  hasBond: boolean; hp: number; attack: number; defense: number; image: string;
  skills: LibrarySkill[]; pveSkills: LibrarySkill[]; skins: LibrarySkin[];
  bondRewards: { FavorLevel: number; Rewards?: Record<string, number> }[];
  breakthrough: { NeedMaterials?: Record<string, number>; Rewards?: Record<string, number> };
}
export interface LibraryCard {
  id: number; name: string; number: string; description: string; comment: string; image: string;
  type: number; cost: number; gallery: boolean; dedicated: boolean; playable: boolean;
  target: number; params: number[]; relatedCards: number[];
}
export interface LibraryAltArt { id: number; cardId: number; name: string; description: string; image: string; animated: boolean }
export interface LibraryMutator { id: number; name: string; description: string; image: string }
export interface HeroProgress { id: number; level: number; exp: number; skin: number; breakthrough: boolean; pveLevel?: number | null; pveExp?: number | null; potential?: number[] }
export interface PlayedMatch { time: number; rank: number; heroId: number; mapId: number; index: number; replayId: string; version: string; detailAvailable?: boolean }
export interface MatchParticipant {
  playerId: number; name: string; heroId: number; rank: number; level: number; gold: number;
  slot: number; giveUp: boolean; playerLevel: number;
  kills?: number | null; deaths?: number | null; damage?: number | null; taken?: number | null;
  healing?: number | null; coins?: number | null; transfer?: number | null; turns?: number | null;
}
export interface MatchDetail {
  requestId: string; playerId: number; index: number; time: number; heroId: number; mapId: number;
  status: 'loading' | 'ready' | 'error'; error: string; message?: string; replayId?: string; version?: string; participants: MatchParticipant[]; observedAt: string;
  statsSource: string; statsError: string; arenaId?: number | null; rounds?: number | null; durationSeconds?: number | null;
  refreshError?: string;
}
export interface LibraryAccount {
  platform: string; role: string; observedAt: string;
  snapshot: { playerId: number; name: string; level: number; exp: number; heroes: HeroProgress[]; skins: number[]; altArts: number[]; matches: PlayedMatch[] | null };
}
export interface LibrarySession { connectionId: string; platformId: string; livePlayerId: number | null; boundRole: string; connectionState: string; runtimeVersion: string }
export const gameCatalog = rawCatalog as unknown as {
  version: string; source: string; configSha256: string; characters: LibraryCharacter[];
  cards: LibraryCard[]; altArts: LibraryAltArt[]; mutators: LibraryMutator[];
  favorLevels: { Id?: number; NeedExp?: number; TotalExp: number }[];
  playerLevels: { Level: number; NeedExp: number }[];
  maps: { id: number; name: string }[]; modes: { id: number; name: string; group: number }[];
  assets: Record<string, string>; itemNames?: Record<string, string>;
};
export const cardTypes = ['其他', '攻击', '防御', '效果', '反制', '事件', '幸运', '厄运', '诅咒', '建筑'];
export const accountKey = (a: LibraryAccount) => `${a.platform}:${a.snapshot.playerId}`;
export const sessionKey = (s: LibrarySession) => `${s.platformId}:${s.livePlayerId}`;
export const plainGameText = (text: string) => text.replace(/\{[A-Za-z][A-Za-z0-9_]*=([^{}]+)\}/g, '$1').replace(/<sprite[^>]*>/gi, ' ◆ ').replace(/<[^>]*>/g, '').replace(/\[(?:\/?color(?:=[^\]]+)?|\/?b|\/?i|\/?size(?:=[^\]]+)?)\]/gi, '').replace(/\\n/g, '\n');
export const characterName = (id: number) => gameCatalog.characters.find(c => c.id === id)?.name || `角色 #${id}`;
export const isPreviewCharacter = (c: LibraryCharacter) => !c.name.trim();
export const libraryCharacterName = (c: LibraryCharacter) => c.name.trim() || `预更新角色 #${c.id}`;
export const platformName = (platform: string) => ({ STEAM: 'Steam', STEAM_CN: 'Steam 国服', STEAM_GLOBAL: 'Steam 国际服', TAPTAP: 'TapTap', BILIBILI: '哔哩哔哩' }[platform] || platform);

/** Never silently use a spectator's data for an unsynchronized live main account. */
export function defaultLibraryKey(accounts: LibraryAccount[], sessions: LibrarySession[], saved: string | null) {
  if (saved && (accounts.some(a => accountKey(a) === saved) || sessions.some(s => sessionKey(s) === saved))) return saved;
  const main = sessions.find(s => s.boundRole === 'MAIN' && s.livePlayerId && s.connectionState === 'CONNECTED');
  if (main) return sessionKey(main);
  const cachedMain = accounts.find(a => a.role === 'MAIN');
  return cachedMain ? accountKey(cachedMain) : '';
}
export function matchDate(time: number) { return new Date(time < 1e12 ? time * 1000 : time).toLocaleString('zh-CN'); }
