import { characterName, gameCatalog, pickStaticEmoteAsset } from '../../../shared/gameLibrary.ts';
import type { ReplayChipInfo, ReplayPlayerReport } from '../../../shared/webCloudClient.ts';

export const PLAYER_COLORS = ['#ec4899', '#3b82f6', '#8b5cf6', '#10b981'];

export const SAMPLE_REPLAY_ID = '1794507512315356';
export const DEFAULT_SERVER = 'cn';

export function playerColor(slot: number): string {
  return PLAYER_COLORS[Math.max(0, (slot - 1) % PLAYER_COLORS.length)];
}

export function playerSeatColor(
  player: Pick<ReplayPlayerReport, 'slot' | 'label'> | undefined,
  fallbackSlot = 1,
): string {
  if (player) {
    const match = /^P(\d+)$/i.exec(player.label || '');
    if (match) return playerColor(Number(match[1]));
    if (player.slot >= 1 && player.slot <= 4) return playerColor(player.slot);
  }
  return playerColor(fallbackSlot);
}

export function formatDuration(sec: number): string {
  if (!Number.isFinite(sec) || sec <= 0) return '—';
  const total = Math.round(sec);
  const mins = Math.floor(total / 60);
  const rem = total % 60;
  if (mins <= 0) return `${rem} 秒`;
  if (rem === 0) return `${mins} 分钟`;
  return `${mins} 分 ${rem} 秒`;
}

export function formatTime(ts: number): string {
  if (!ts) return '—';
  const d = new Date(ts > 1e12 ? ts : ts * 1000);
  if (Number.isNaN(d.getTime())) return '—';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getMonth() + 1}/${d.getDate()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function formatInt(v: number | null | undefined): string {
  if (v == null || !Number.isFinite(v)) return '—';
  return Math.round(v).toLocaleString('zh-CN');
}

export function formatSignedInt(v: number | null | undefined): string {
  if (v == null || !Number.isFinite(v)) return '—';
  const n = Math.round(v);
  if (n > 0) return `+${n.toLocaleString('zh-CN')}`;
  return n.toLocaleString('zh-CN');
}

export function formatPercent(v: number | null | undefined): string {
  if (v == null || !Number.isFinite(v)) return '无机会';
  return `${Math.round(v)}%`;
}

export function formatRatio(v: number | null | undefined): string {
  if (v == null || !Number.isFinite(v)) return '—';
  return v.toFixed(2);
}

export function playerSummaryLine(players: ReplayPlayerReport[]): string {
  return players.map((p) => `${p.label} ${p.heroName}·${p.nick}`).join(' / ');
}

export function heroImageKey(heroId: number, seed: number | string = heroId): string {
  return pickStaticEmoteAsset(heroId, seed);
}

export function heroDisplayName(player: ReplayPlayerReport): string {
  return player.heroName || characterName(player.heroId);
}

type CatalogCardLite = (typeof gameCatalog.cards)[number];

const cardIndex = new Map<number, CatalogCardLite>(
  (gameCatalog.cards || []).map((c) => [c.id, c]),
);

const chipIndex = new Map<number, { id: number; name: string; image?: string; quality?: number }>(
  (((gameCatalog as unknown as { chips?: Array<{ id: number; name: string; image?: string; quality?: number }> }).chips) || []).map((c) => [c.id, c]),
);

export function lookupCard(cardId: number): { id: number; name: string; description?: string; image?: string } {
  return cardIndex.get(cardId) || { id: cardId, name: `卡牌 #${cardId}` };
}

export function lookupChip(chipId: number, fallback?: ReplayChipInfo): ReplayChipInfo {
  const fromCatalog = chipIndex.get(chipId);
  if (fallback) {
    return {
      ...fallback,
      image: fallback.image || fromCatalog?.image,
      quality: fallback.quality || fromCatalog?.quality,
    };
  }
  return {
    id: chipId,
    name: fromCatalog?.name || `筹码 #${chipId}`,
    image: fromCatalog?.image,
    quality: fromCatalog?.quality,
  };
}

const KIND_META: Record<string, { label: string; tone: string }> = {
  roll: { label: '掷骰', tone: 'neutral' },
  move: { label: '移动', tone: 'info' },
  shop: { label: '商店', tone: 'gold' },
  transfer: { label: '代付', tone: 'gold' },
  battle: { label: '战斗', tone: 'danger' },
  skill: { label: '技能', tone: 'accent' },
  card: { label: '出牌', tone: 'accent' },
  chip: { label: '筹码', tone: 'ok' },
  mission: { label: '任务', tone: 'ok' },
  round: { label: '轮次', tone: 'neutral' },
  star: { label: '升星', tone: 'gold' },
};

export function stepKindMeta(kind: string): { label: string; tone: string } {
  return KIND_META[kind] || { label: kind || '事件', tone: 'neutral' };
}

export function chipQualityTone(quality: string | number | undefined): string {
  const q = String(quality ?? '').toLowerCase();
  if (q.includes('3') || q.includes('金') || q.includes('ssr') || q.includes('legend')) return 'gold';
  if (q.includes('2') || q.includes('紫') || q.includes('sr') || q.includes('rare')) return 'accent';
  if (q.includes('1') || q.includes('蓝') || q.includes('r')) return 'info';
  return 'neutral';
}
