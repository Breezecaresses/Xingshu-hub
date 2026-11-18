export type RoomMode = 'PVE' | 'PVP' | 'COOP_EVENT' | 'SELF_HOSTED';
export type HostTarget = 'OFFICIAL' | 'SELF_HOSTED';
export type ThinkTime = '快' | '中' | '慢';
export type RoomPrivacy = 'PUBLIC' | 'FRIENDS' | 'PASSWORD';
export type RoomPreviewStatus = 'IDLE' | 'RECRUITING' | 'READY_FULL';

export const OFFICIAL_DIFFICULTIES = ['普通', '困难', '噩梦', '疯狂', '极限'] as const;
export const THINK_TIME_OPTIONS: ThinkTime[] = ['快', '中', '慢'];
export const ROOM_TAG_OPTIONS = ['休闲', '速刷', '冲刺', '教学', '娱乐'] as const;

export const ZERO_YIELD_NOTICE_ZH =
  '0 收益（服务器自托管托管模式 / 娱乐演练：不结算官方经验、星币、角色好感或任务进度）';
export const ZERO_YIELD_NOTICE_EN =
  '0 Yield (Self-Hosted Server / Sandbox Mode: no official EXP, Star Coins, Bond, or Mission progress)';

export interface RoomMapOption {
  id: number;
  name: string;
  enName: string;
  mode: RoomMode;
  difficulties: string[];
  officialAvailable: boolean;
}

export interface SquadSlot {
  seat: 1 | 2 | 3 | 4;
  occupied: boolean;
  isHost: boolean;
  isSelf?: boolean;
  playerName: string;
  uid: string;
  characterName: string;
  characterTitle: string;
  ready: boolean;
}

export interface RoomConfigDraft {
  hostTarget: HostTarget;
  mode: RoomMode;
  mapId: number;
  difficulty: string;
  thinkTime: ThinkTime;
  roomTag: string;
  maxPlayers: 2 | 3 | 4;
  privacy: RoomPrivacy;
  password: string;
  roomName: string;
  note: string;
}

export interface RoomPreviewSnapshot {
  roomCode: string;
  status: RoomPreviewStatus;
  config: RoomConfigDraft;
  slots: SquadSlot[];
  updatedAtLabel: string;
}

export const ROOM_MAPS: RoomMapOption[] = [
  // PVE 合作（官方现役，按官方 4/5 档难度映射）
  { id: 82007, name: '星趴·梦想号', enName: 'Astral Express: Dream', mode: 'PVE', difficulties: ['普通', '困难', '噩梦', '疯狂', '极限'], officialAvailable: true },
  { id: 82008, name: '御魂庆典', enName: 'Mitama Festival', mode: 'PVE', difficulties: ['普通', '困难', '噩梦', '疯狂', '极限'], officialAvailable: true },
  { id: 82010, name: '水乡古镇', enName: 'Water Town', mode: 'PVE', difficulties: ['普通', '困难', '噩梦', '疯狂', '极限'], officialAvailable: true },
  { id: 82013, name: '龙宫游乐园', enName: 'Dragon Palace', mode: 'PVE', difficulties: ['普通', '困难', '噩梦', '疯狂'], officialAvailable: true },
  { id: 82014, name: '幽魂暗巷', enName: 'Phantom Alley', mode: 'PVE', difficulties: ['普通', '困难', '噩梦', '疯狂'], officialAvailable: true },
  { id: 82015, name: '园林中庭', enName: 'Garden Courtyard', mode: 'PVE', difficulties: ['普通', '困难', '噩梦', '疯狂'], officialAvailable: true },
  { id: 82016, name: '异变图书馆', enName: 'Anomaly Library', mode: 'PVE', difficulties: ['普通', '困难', '噩梦', '疯狂'], officialAvailable: true },
  { id: 82017, name: '森秋幻境', enName: 'Autumn Forest', mode: 'PVE', difficulties: ['普通', '困难', '噩梦', '疯狂'], officialAvailable: true },
  // PVP 对战（官方现役）
  { id: 81003, name: '游乐园', enName: 'Amusement Park', mode: 'PVP', difficulties: [], officialAvailable: true },
  { id: 81005, name: '樱花小镇', enName: 'Sakura Town', mode: 'PVP', difficulties: [], officialAvailable: true },
  { id: 81004, name: '十字路口站', enName: 'Crossroad Station', mode: 'PVP', difficulties: [], officialAvailable: true },
  { id: 81006, name: '魅影都市', enName: 'Phantom City', mode: 'PVP', difficulties: [], officialAvailable: true },
  { id: 81007, name: '梦想号-上层甲板', enName: 'Dream Upper Deck', mode: 'PVP', difficulties: [], officialAvailable: true },
  { id: 81011, name: '丛林冒险', enName: 'Jungle Adventure', mode: 'PVP', difficulties: [], officialAvailable: true },
  // 合作活动（游戏内暂未开放 / 绝版限时，归类为 0 收益自托管）
  { id: 82009, name: '礼物广场', enName: 'Gift Plaza', mode: 'COOP_EVENT', difficulties: [], officialAvailable: false },
  { id: 82012, name: '魔法学院', enName: 'Magic Academy', mode: 'COOP_EVENT', difficulties: [], officialAvailable: false },
  { id: 83001, name: '海选赛运动场', enName: 'Qualifier Stadium', mode: 'COOP_EVENT', difficulties: [], officialAvailable: false },
  { id: 83002, name: '淘汰赛运动场', enName: 'Knockout Stadium', mode: 'COOP_EVENT', difficulties: [], officialAvailable: false },
  { id: 83003, name: '决赛大赛场', enName: 'Finals Arena', mode: 'COOP_EVENT', difficulties: [], officialAvailable: false },
  // 星枢自托管模式（自定义 / 绝版复刻演练，0 收益）
  { id: 99001, name: '星枢自定义沙盒场', enName: 'XingShu Custom Sandbox', mode: 'SELF_HOSTED', difficulties: ['普通', '困难', '噩梦', '疯狂', '极限'], officialAvailable: false },
  { id: 99002, name: '绝版活动复刻演练', enName: 'Retired Event Replay', mode: 'SELF_HOSTED', difficulties: ['普通', '困难', '噩梦', '疯狂'], officialAvailable: false },
];

export const SAMPLE_CHARACTERS = [
  { name: '帕露南', title: '商业之主' },
  { name: '姬梦枫', title: '苍蓝之星' },
  { name: '米米', title: '爆破专家' },
  { name: '恋', title: '命运骰手' },
  { name: '南希露', title: '暗影游侠' },
  { name: '美咲', title: '疾风剑士' },
];

export const ROOM_TITLE_PRESETS = [
  '日常刷本速车 · 来不抢输出',
  '萌新友好教学 · 欢迎上麦',
  '高难冲层车 · 自备满配芯片',
  '自托管 0收益娱乐局 · 拒绝压力怪',
];

export function createDefaultDraft(): RoomConfigDraft {
  return {
    hostTarget: 'OFFICIAL',
    mode: 'PVE',
    mapId: 82007,
    difficulty: '噩梦',
    thinkTime: '中',
    roomTag: '速刷',
    maxPlayers: 4,
    privacy: 'PASSWORD',
    password: '2580',
    roomName: '日常刷本速车 · 来不抢输出',
    note: '可语音可打字，缺一辅助位',
  };
}

export function getMapById(mapId: number): RoomMapOption {
  return ROOM_MAPS.find((m) => m.id === mapId) ?? ROOM_MAPS[0];
}

export function isSelfHostedZeroYield(config: Pick<RoomConfigDraft, 'hostTarget' | 'mapId' | 'mode'>): boolean {
  const map = getMapById(config.mapId);
  return config.hostTarget === 'SELF_HOSTED' || !map.officialAvailable || config.mode === 'COOP_EVENT' || config.mode === 'SELF_HOSTED';
}

export function sanitizeNumericPassword(raw: string): string {
  return raw.replace(/\D+/g, '').slice(0, 6);
}

export function effectivePrivacy(config: Pick<RoomConfigDraft, 'privacy' | 'password'>): RoomPrivacy {
  if (config.privacy === 'PASSWORD' && sanitizeNumericPassword(config.password).length === 0) {
    return 'PUBLIC';
  }
  return config.privacy;
}

export function buildPaKaiFangCommand(draft: RoomConfigDraft): string {
  const map = getMapById(draft.mapId);
  const modeToken =
    draft.mode === 'COOP_EVENT'
      ? '活动'
      : draft.mode === 'SELF_HOSTED'
        ? '自托管'
        : draft.mode;
  const parts = ['趴开房', modeToken, map.name];
  if (draft.difficulty && map.difficulties.length > 0) {
    parts.push(draft.difficulty);
  }
  parts.push(`思考:${draft.thinkTime}`);
  if (draft.roomTag.trim()) {
    parts.push(`标签:${draft.roomTag.trim()}`);
  }
  parts.push(`${draft.maxPlayers}人`);
  const cleanPwd = sanitizeNumericPassword(draft.password);
  const privacy = effectivePrivacy(draft);
  if (privacy === 'FRIENDS') {
    parts.push('仅好友');
  } else if (privacy === 'PASSWORD') {
    parts.push(`密码:${cleanPwd}`);
  } else {
    parts.push('公开');
  }
  if (isSelfHostedZeroYield(draft)) {
    parts.push('自托管0收益');
  }
  if (draft.roomName.trim()) {
    parts.push(draft.roomName.trim());
  }
  return parts.join(' ');
}

export function buildSlotsForStatus(status: RoomPreviewStatus, maxPlayers: 2 | 3 | 4): SquadSlot[] {
  const base: SquadSlot[] = [
    {
      seat: 1,
      occupied: status !== 'IDLE',
      isHost: status !== 'IDLE',
      isSelf: status !== 'IDLE',
      playerName: '星枢旅人',
      uid: '10482910',
      characterName: '帕露南',
      characterTitle: '商业之主',
      ready: status !== 'IDLE',
    },
    {
      seat: 2,
      occupied: status === 'RECRUITING' || status === 'READY_FULL',
      isHost: false,
      playerName: '枫糖布丁',
      uid: '10938421',
      characterName: '姬梦枫',
      characterTitle: '苍蓝之星',
      ready: true,
    },
    {
      seat: 3,
      occupied: status === 'READY_FULL',
      isHost: false,
      playerName: '夜风星愿',
      uid: '11209344',
      characterName: '米米',
      characterTitle: '爆破专家',
      ready: status === 'READY_FULL',
    },
    {
      seat: 4,
      occupied: status === 'READY_FULL' && maxPlayers >= 4,
      isHost: false,
      playerName: '晚安星海',
      uid: '11890452',
      characterName: '恋',
      characterTitle: '命运骰手',
      ready: status === 'READY_FULL',
    },
  ];

  return base.map((slot) => {
    if (slot.seat > maxPlayers) {
      return {
        ...slot,
        occupied: false,
        isHost: false,
        isSelf: false,
        playerName: '',
        uid: '',
        characterName: '',
        characterTitle: '',
        ready: false,
      };
    }
    if (!slot.occupied) {
      return {
        ...slot,
        isHost: false,
        isSelf: false,
        playerName: '',
        uid: '',
        characterName: '',
        characterTitle: '',
        ready: false,
      };
    }
    return slot;
  });
}

export function createInitialRoomSnapshot(): RoomPreviewSnapshot {
  const config = createDefaultDraft();
  return {
    roomCode: '849206',
    status: 'RECRUITING',
    config,
    slots: buildSlotsForStatus('RECRUITING', config.maxPlayers),
    updatedAtLabel: '刚刚 · 模板预览态',
  };
}
