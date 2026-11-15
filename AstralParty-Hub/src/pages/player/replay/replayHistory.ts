import type { ReplayAnalysisReport } from '../../../shared/webCloudClient';
import { playerSummaryLine } from './replayShared';

const HISTORY_KEY = 'xingshu.replay.history.v1';
const MAX_HISTORY = 30;

export interface ReplayHistoryRecord {
  key: string;
  replayId: string;
  server: string;
  source: 'id' | 'upload';
  mapName: string;
  difficultyName: string;
  rounds: number;
  win: boolean;
  savedAt: number;
  favorite: boolean;
  note: string;
  playersSummary: string;
}

export function historyKeyFor(replayId: string, server: string): string {
  return `${server || 'cn'}:${replayId}`;
}

export function loadReplayHistory(): ReplayHistoryRecord[] {
  try {
    if (typeof localStorage === 'undefined') return [];
    const raw = localStorage.getItem(HISTORY_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((item): item is ReplayHistoryRecord => Boolean(item && typeof item.key === 'string'));
  } catch {
    return [];
  }
}

function saveReplayHistory(items: ReplayHistoryRecord[]): void {
  try {
    if (typeof localStorage === 'undefined') return;
    localStorage.setItem(HISTORY_KEY, JSON.stringify(items.slice(0, MAX_HISTORY)));
  } catch {
    // Ignore storage quota errors
  }
}

export function upsertReplayHistory(
  report: ReplayAnalysisReport,
  source: 'id' | 'upload',
): ReplayHistoryRecord[] {
  const list = loadReplayHistory();
  const key = historyKeyFor(report.replayId, report.server);
  const prev = list.find((item) => item.key === key);
  const next: ReplayHistoryRecord = {
    key,
    replayId: report.replayId,
    server: report.server || 'cn',
    source,
    mapName: report.meta.mapName || `地图 ${report.meta.mapId}`,
    difficultyName: report.meta.difficultyName || '',
    rounds: report.meta.rounds,
    win: report.meta.win,
    savedAt: Date.now(),
    favorite: prev?.favorite ?? false,
    note: prev?.note ?? '',
    playersSummary: playerSummaryLine(report.players),
  };
  const updated = [next, ...list.filter((item) => item.key !== key)].slice(0, MAX_HISTORY);
  saveReplayHistory(updated);
  return updated;
}

export function toggleReplayFavorite(key: string): ReplayHistoryRecord[] {
  const updated = loadReplayHistory().map((item) =>
    item.key === key ? { ...item, favorite: !item.favorite } : item,
  );
  saveReplayHistory(updated);
  return updated;
}

export function updateReplayNote(key: string, note: string): ReplayHistoryRecord[] {
  const clean = note.slice(0, 200);
  const updated = loadReplayHistory().map((item) =>
    item.key === key ? { ...item, note: clean } : item,
  );
  saveReplayHistory(updated);
  return updated;
}

export function removeReplayHistory(key: string): ReplayHistoryRecord[] {
  const updated = loadReplayHistory().filter((item) => item.key !== key);
  saveReplayHistory(updated);
  return updated;
}
