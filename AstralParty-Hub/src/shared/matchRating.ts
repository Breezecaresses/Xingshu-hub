import type { MatchParticipant } from './gameLibrary';

export const ratingMethod = '星枢评分 v2：仅比较同一局玩家，取综合分、输出分、辅助分中的最高分，再除以（1＋死亡次数 × 0.15）。各项比例均为本人数据 ÷ 本局最高值。综合分：输出 30%、治疗 20%、击杀 15%、转账 30%、累计金币 5%，满分 10；全局为零的项目移除权重。输出分：输出 75%、击杀 25%，满分 10，同样移除全局为零的项目。辅助分：min（转账 ÷ 20，1）×（6＋2 × 转账比例＋2 × 治疗比例），转账达到 20 时有 6 分基础贡献，死亡仍会扣分。这是星枢计算的参考分，不是游戏官方评分。';

/** Missing replay metrics remain unknown; a profile-only record never gets a fabricated score. */
export function rateMatch(players: MatchParticipant[]): (number | null)[] {
  const required = ['kills', 'deaths', 'damage', 'healing', 'coins', 'transfer'] as const;
  if (!players.length || players.some(p => required.some(k => p[k] == null || !Number.isFinite(p[k]) || p[k]! < 0))) return players.map(() => null);
  const rows = players.map(p => [p.damage!, p.healing!, p.kills!, p.transfer!, p.coins!]);
  const max = [0, 1, 2, 3, 4].map(i => Math.max(...rows.map(r => r[i])));
  const weights = [0.3, 0.2, 0.15, 0.3, 0.05];
  const active = weights.reduce((n, w, i) => n + (max[i] > 0 ? w : 0), 0);
  if (!active) return players.map(() => null);
  const offenseActive = (max[0] > 0 ? 0.75 : 0) + (max[2] > 0 ? 0.25 : 0);
  return rows.map((r, j) => {
    const ratios = r.map((v, i) => max[i] > 0 ? v / max[i] : 0);
    const balanced = 10 * ratios.reduce((n, v, i) => n + weights[i] * v, 0) / active;
    const offense = offenseActive ? 10 * (0.75 * ratios[0] + 0.25 * ratios[2]) / offenseActive : 0;
    const support = Math.min(players[j].transfer! / 20, 1) * (6 + 2 * ratios[3] + 2 * ratios[1]);
    return Math.round(100 * Math.max(balanced, offense, support) / (1 + players[j].deaths! * 0.15)) / 100;
  });
}

export function matchModeName(id: number, modes: {id:number;name:string}[]) {
  return modes.find(m => m.id === id)?.name || `模式 #${id}`;
}
