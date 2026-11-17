import type { CloudAccount } from '../pages/account/cloudLoginStatus';
import type { LibraryAccount, MatchDetail } from './gameLibrary';
import { webApiBaseUrl } from './runtime.ts';

const TOKEN_KEY = 'xingshu.web.cloudToken';
const DEVICE_KEY = 'xingshu.web.deviceId';

export interface WebCloudGrant {
  requestId: string;
  verifier: string;
  loginUrl: string;
  qrPng: string;
  expiresIn: number;
}

export interface WebCloudState {
  loaded: boolean;
  account: CloudAccount | null;
  libraryAccount: LibraryAccount | null;
  observedAt: string | null;
}

export interface PublicPlayerMatch {
  time: number;
  rank: number;
  heroId: number;
  mapId: number;
  index?: number;
  replayId?: string;
  version?: string;
}

export interface PublicPlayerSnapshot {
  uid: string;
  nick: string;
  level: number;
  online: boolean;
  busy?: boolean;
  observedAt?: string;
  praise?: number;
  career?: {
    matches?: number;
    wins?: number;
    heroes?: number;
    adorns?: number;
    skins?: number;
  };
  matches?: PublicPlayerMatch[];
}

export interface ReplayChipInfo {
  id: number;
  name: string;
  quality?: string | number;
  qualityName?: string;
  desc?: string;
  description?: string;
  tag?: string;
  image?: string;
}

export interface ReplayTransferEvent {
  index: number;
  round: number;
  fromPid: string;
  fromLabel: string;
  fromName: string;
  toPid: string;
  toLabel: string;
  toName: string;
  amount: number;
  source: string;
  stepIndex: number;
}

export interface ReplayTransferOpportunity {
  round: number;
  pid: string;
  playerLabel: string;
  playerName: string;
  gold: number;
  roll: number;
  fromNode: number;
  reachableShops: number[];
  openedShop: boolean;
  transferred: boolean;
  paid: boolean;
  missedShop: boolean;
  isRemote?: boolean;
}

export interface ReplayPlayerReport {
  id: string;
  pid: number;
  slot: number;
  label: string;
  nick: string;
  heroId: number;
  heroName: string;
  level: number;
  initGold: number;
  killCount: number;
  totalDamage: number;
  realDamage: number;
  taskMonsterDamage: number;
  bossDamage: number;
  totalDie: number;
  totalInjured: number;
  treatmentScore: number;
  movePoint: number;
  totalGold: number;
  transferOut: number;
  transferIn: number;
  stolenGold: number;
  starSpend: number;
  netEconomy: number;
  conversionRatio: number;
  pre6PayCount: number;
  pre6OppCount: number;
  pre6Willingness: number | null;
  missedShopRounds: number[];
  finalKillBoss: boolean;
  relics: ReplayChipInfo[];
}

export interface ReplayMissionItem {
  id: number;
  title: string;
  isMonsterMission: boolean;
  progress: number;
  total: number;
  appearedRound: number;
  completedRound: number | null;
  damageByPlayer: Record<string, number | null>;
}

export interface ReplayChipEvent {
  round: number;
  pid: string;
  playerLabel: string;
  playerName: string;
  source: string;
  order: number;
  chosenId: number;
  chosen: ReplayChipInfo;
  rerollCount: number;
  pools: ReplayChipInfo[][];
}

export interface ReplayStepPlayerState {
  pid: string;
  node: number;
  hp: number;
  maxHp: number;
  gold: number;
  stars: number;
  atk: number;
  def: number;
  cards: number[];
  relics: number[];
}

export interface ReplayStepMonsterState {
  id: number;
  heroId: number;
  name: string;
  isBoss: boolean;
  node: number;
  hp: number;
  maxHp: number;
}

export interface ReplayStep {
  index: number;
  round: number;
  kind: string;
  actorPid: string;
  targetPid: string;
  title: string;
  detail: string;
  players: ReplayStepPlayerState[];
  monsters: ReplayStepMonsterState[];
}

export interface ReplayRoundTrendPlayerStats {
  totalDamage: number;
  killCount: number;
  totalGold: number;
  movePoint: number;
  totalInjured: number;
  totalDie: number;
}

export interface ReplayRoundTrendItem {
  round: number;
  players: Record<string, ReplayRoundTrendPlayerStats>;
}

export interface ReplayRoundEconomyPlayerStats {
  monster: number;
  transfer: number;
  chip: number;
  other: number;
  out: number;
  net: number;
}

export interface ReplayRoundEconomyItem {
  round: number;
  label: string;
  players: Record<string, ReplayRoundEconomyPlayerStats>;
}

export interface ReplayBoardNode {
  id: number;
  type: number;
  label: string;
  icon?: string;
  isHome: boolean;
  isShop: boolean;
  x: number;
  y: number;
  gx?: number;
  gy?: number;
  spawn?: number;
  neighbors?: number[];
  jumpTo?: number;
  initDir?: number;
}

export interface ReplayAnalysisReport {
  replayId: string;
  server: string;
  meta: {
    mapId: number;
    mapName: string;
    difficulty: number;
    difficultyName: string;
    modeId: number;
    modeName: string;
    version: string;
    rounds: number;
    durationSec: number;
    win: boolean;
    roomMode: string;
    startTime: number;
    finishTime: number;
    watchCode?: string;
  };
  players: ReplayPlayerReport[];
  transfers: ReplayTransferEvent[];
  transferSummary: {
    count: number;
    totalGold: number;
    missedCount: number;
    opportunityCount: number;
    opportunities: ReplayTransferOpportunity[];
  };
  missions: ReplayMissionItem[];
  roundTrends: ReplayRoundTrendItem[];
  roundEconomy: ReplayRoundEconomyItem[];
  chipEvents: ReplayChipEvent[];
  highlights: string[];
  board: {
    layoutMode?: string;
    sceneKey?: string;
    gridWidth?: number;
    gridHeight?: number;
    minSpacing?: number;
    cellSize?: number;
    nodes: ReplayBoardNode[];
    edges: Array<[number, number]>;
    shopNodes: number[];
  };
  steps: ReplayStep[];
}

function getToken(): string | null {
  try {
    return typeof localStorage !== 'undefined' ? localStorage.getItem(TOKEN_KEY) : null;
  } catch {
    return null;
  }
}

function setToken(token: string | null): void {
  try {
    if (typeof localStorage === 'undefined') return;
    if (token) localStorage.setItem(TOKEN_KEY, token);
    else localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Ignore storage quota errors
  }
}

function getOrCreateDeviceId(): string {
  try {
    const existing = typeof localStorage !== 'undefined' ? localStorage.getItem(DEVICE_KEY) : null;
    if (existing && existing.length >= 16) return existing;
    const raw =
      typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? crypto.randomUUID().replace(/-/g, '')
        : `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`.padEnd(24, '0');
    const id = `web-${raw}`;
    localStorage?.setItem(DEVICE_KEY, id);
    return id;
  } catch {
    return `web-${Date.now().toString(36)}-fallback-device-id`;
  }
}

function randomVerifier(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

async function pkceChallenge(verifier: string): Promise<string> {
  const data = new TextEncoder().encode(verifier);
  const digest = await crypto.subtle.digest('SHA-256', data);
  const bytes = new Uint8Array(digest);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function apiRequest<T>(path: string, init: RequestInit = {}, auth = false): Promise<T> {
  const base = webApiBaseUrl();
  const headers = new Headers(init.headers);
  if (auth) {
    const token = getToken();
    if (!token) throw new Error('尚未登录星枢云端账号');
    headers.set('Authorization', `Bearer ${token}`);
  }
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  let res: Response;
  let text: string;
  try {
    res = await fetch(`${base}${path}`, { ...init, headers });
    text = await res.text();
  } catch {
    throw new Error('无法连接星枢云端，请检查网络后重试。');
  }
  let parsed: Record<string, unknown> = {};
  try {
    parsed = text ? (JSON.parse(text) as Record<string, unknown>) : {};
  } catch {
    if (!res.ok) throw new Error(`请求失败（HTTP ${res.status}）`);
  }
  if (res.status === 401 && auth) {
    setToken(null);
    updateWebState({ loaded: true, account: null, libraryAccount: null, observedAt: null });
    throw new Error('云端登录状态已过期，请重新扫码登录');
  }
  if (!res.ok) {
    const msg =
      (typeof parsed.error === 'string' && parsed.error) ||
      (typeof parsed.message === 'string' && parsed.message) ||
      `请求失败（HTTP ${res.status}）`;
    throw new Error(msg);
  }
  return parsed as unknown as T;
}

let state: WebCloudState = {
  loaded: false,
  account: null,
  libraryAccount: null,
  observedAt: null,
};
const listeners = new Set<() => void>();

function updateWebState(next: Partial<WebCloudState>) {
  state = { ...state, ...next };
  listeners.forEach((fn) => fn());
}

export function subscribeWebCloud(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getWebCloudSnapshot(): WebCloudState {
  return state;
}

export async function refreshWebCloudState(): Promise<WebCloudState> {
  const token = getToken();
  if (!token) {
    updateWebState({ loaded: true, account: null, libraryAccount: null, observedAt: null });
    return state;
  }
  try {
    const me = await apiRequest<{ account: CloudAccount }>('/auth/me', { method: 'GET' }, true);
    const data = await apiRequest<{ identity?: CloudAccount; account?: LibraryAccount | null }>(
      '/account/data',
      { method: 'GET' },
      true,
    );
    updateWebState({
      loaded: true,
      account: me.account,
      libraryAccount: data.account ?? null,
      observedAt: data.account?.observedAt ?? null,
    });
  } catch {
    updateWebState({ loaded: true });
  }
  return state;
}

export async function startWebCloudLogin(): Promise<WebCloudGrant> {
  const verifier = randomVerifier();
  const codeChallenge = await pkceChallenge(verifier);
  const res = await apiRequest<{
    request_id: string;
    login_url: string;
    qr_png: string;
    expires_in: number;
  }>('/auth/device', {
    method: 'POST',
    body: JSON.stringify({
      device_id: getOrCreateDeviceId(),
      code_challenge: codeChallenge,
    }),
  });
  return {
    requestId: res.request_id,
    verifier,
    loginUrl: res.login_url,
    qrPng: res.qr_png,
    expiresIn: res.expires_in || 120,
  };
}

export async function pollWebCloudLogin(
  grant: WebCloudGrant,
): Promise<{ status: string; account?: CloudAccount; message?: string }> {
  const res = await apiRequest<{
    status: string;
    access_token?: string;
    account?: CloudAccount;
    message?: string;
  }>('/auth/device/poll', {
    method: 'POST',
    body: JSON.stringify({
      request_id: grant.requestId,
      code_verifier: grant.verifier,
    }),
  });
  if (res.status === 'succeeded' && res.access_token) {
    setToken(res.access_token);
    await refreshWebCloudState();
  }
  return res;
}

export async function logoutWebCloud(): Promise<void> {
  if (getToken()) {
    await apiRequest('/auth/logout', { method: 'POST', body: '{}' }, true).catch(() => {});
  }
  setToken(null);
  updateWebState({ loaded: true, account: null, libraryAccount: null, observedAt: null });
}

export async function syncWebCloudLibrary(): Promise<void> {
  await apiRequest('/account/data/sync', {
    method: 'POST',
    body: JSON.stringify({ confirm_session_switch: true }),
  }, true);
  await refreshWebCloudState();
}

export async function getWebCloudMatchDetail(params: {
  time: number;
  heroId: number;
  mapId: number;
  read_replay?: boolean;
}): Promise<{ detail: MatchDetail | null }> {
  return apiRequest<{ detail: MatchDetail | null }>(
    '/account/data/match',
    { method: 'POST', body: JSON.stringify(params) },
    true,
  );
}

export async function fetchPublicPlayer(uid: string): Promise<PublicPlayerSnapshot> {
  const clean = uid.trim();
  if (!/^\d{1,18}$/.test(clean)) throw new Error('请输入有效的纯数字游戏 UID');
  const res = await apiRequest<{ player: PublicPlayerSnapshot }>('/api/player/public', {
    method: 'POST',
    body: JSON.stringify({ uid: clean }),
  });
  return res.player;
}

function unwrapReplayReport(
  res: ReplayAnalysisReport | { report?: ReplayAnalysisReport },
): ReplayAnalysisReport {
  if ('report' in res && res.report) return res.report;
  return res as ReplayAnalysisReport;
}

export async function analyzeReplayById(
  replayId: string,
  server = 'cn',
): Promise<ReplayAnalysisReport> {
  const clean = replayId.trim();
  if (!/^[A-Za-z0-9_-]{4,64}$/.test(clean)) {
    throw new Error('请输入有效的回放编号（4~64 位数字或字母）');
  }
  const res = await apiRequest<ReplayAnalysisReport | { report?: ReplayAnalysisReport }>(
    '/api/replay/analyze',
    {
      method: 'POST',
      body: JSON.stringify({ replayId: clean, server }),
    },
  );
  return unwrapReplayReport(res);
}

export async function uploadReplayFile(file: File): Promise<ReplayAnalysisReport> {
  const form = new FormData();
  form.append('file', file, file.name);
  const res = await apiRequest<ReplayAnalysisReport | { report?: ReplayAnalysisReport }>(
    '/api/replay/upload',
    {
      method: 'POST',
      body: form,
    },
  );
  return unwrapReplayReport(res);
}
