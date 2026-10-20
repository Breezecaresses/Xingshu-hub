type Mode = { id: number; group: number };
type Match = { rank: number; mapId: number };

/** The game's mode group identifies cooperative (1) and competitive (2) results. */
export function matchResult(rank: number, modeId: number, modes: readonly Mode[]) {
  const group = modes.find(m => m.id === modeId)?.group;
  const kind = group === 1 ? 'pve' : group === 2 ? 'pvp' : 'unknown';
  const known = kind !== 'unknown' && Number.isInteger(rank) && rank > 0;
  const outcome = !known ? 'unknown' : rank === 1 ? 'win' : 'loss';
  const label = !known ? '—' : kind === 'pve' ? (outcome === 'win' ? '胜利' : '失败') : `#${rank}`;
  const caption = !known ? '结果未知' : kind === 'pve' ? '对局结果' : rank === 1 ? '第一名' : '最终名次';
  return { kind, outcome, label, caption };
}

export function matchHistorySummary(records: readonly Match[], modes: readonly Mode[]) {
  let wins = 0, decidedMatches = 0, pvpMatches = 0, pvpRankSum = 0;
  for (const record of records) {
    const result = matchResult(record.rank, record.mapId, modes);
    if (result.outcome === 'unknown') continue;
    decidedMatches++;
    if (result.outcome === 'win') wins++;
    if (result.kind === 'pvp') { pvpMatches++; pvpRankSum += record.rank; }
  }
  return {
    wins, decidedMatches, pvpMatches,
    winRate: decidedMatches ? wins / decidedMatches * 100 : null,
    averagePvpRank: pvpMatches ? pvpRankSum / pvpMatches : null,
  };
}
