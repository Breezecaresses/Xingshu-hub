import React, { useMemo, useState } from 'react';
import { ArrowUpRight, Coins, Cpu, Flag, ListChecks, Sparkles, TrendingUp } from 'lucide-react';
import type {
  ReplayAnalysisReport,
  ReplayRoundTrendPlayerStats,
} from '../../../shared/webCloudClient';
import { GameImage } from '../LibraryUI';
import {
  formatInt,
  formatPercent,
  formatRatio,
  formatSignedInt,
  heroDisplayName,
  heroImageKey,
  playerSeatColor,
} from './replayShared';

type TrendMetric = keyof ReplayRoundTrendPlayerStats;
type EconomySortKey =
  | 'slot'
  | 'totalDamage'
  | 'netEconomy'
  | 'totalGold'
  | 'starSpend'
  | 'conversionRatio'
  | 'movePoint'
  | 'pre6Willingness';

const TREND_METRICS: Array<{ key: TrendMetric; label: string }> = [
  { key: 'totalDamage', label: '输出伤害' },
  { key: 'killCount', label: '击倒数' },
  { key: 'totalGold', label: '累计星币' },
  { key: 'movePoint', label: '移动点数' },
  { key: 'totalInjured', label: '承受伤害' },
  { key: 'totalDie', label: '倒下次数' },
];

export interface ReplayOverviewTabsProps {
  report: ReplayAnalysisReport;
  onSwitchTab: (tab: 'overview' | 'board' | 'chips' | 'report' | 'export') => void;
  onJumpToStep: (stepIndex: number) => void;
}

export const ReplayOverviewTabs: React.FC<ReplayOverviewTabsProps> = ({
  report,
  onSwitchTab,
  onJumpToStep,
}) => {
  const [transferView, setTransferView] = useState<'actual' | 'opportunities'>('actual');
  const [transferPlayerFilter, setTransferPlayerFilter] = useState<string>('all');
  const [transferRoundFilter, setTransferRoundFilter] = useState<string>('all');
  const [trendMetric, setTrendMetric] = useState<TrendMetric>('totalDamage');
  const [ecoSortKey, setEcoSortKey] = useState<EconomySortKey>('slot');
  const [ecoSortDir, setEcoSortDir] = useState<'asc' | 'desc'>('asc');

  const topDamage = useMemo(
    () => Math.max(0, ...report.players.map((p) => p.totalDamage || 0)),
    [report.players],
  );

  const sortedEconomyPlayers = useMemo(() => {
    const list = [...report.players];
    list.sort((a, b) => {
      const av = (a[ecoSortKey] ?? -999999) as number;
      const bv = (b[ecoSortKey] ?? -999999) as number;
      if (av === bv) return a.slot - b.slot;
      return ecoSortDir === 'asc' ? av - bv : bv - av;
    });
    return list;
  }, [report.players, ecoSortKey, ecoSortDir]);

  const toggleEcoSort = (key: EconomySortKey) => {
    if (ecoSortKey === key) {
      setEcoSortDir((d) => (d === 'desc' ? 'asc' : 'desc'));
    } else {
      setEcoSortKey(key);
      setEcoSortDir(key === 'slot' ? 'asc' : 'desc');
    }
  };

  const ecoArrow = (key: EconomySortKey) => {
    if (ecoSortKey !== key) return '↕';
    return ecoSortDir === 'desc' ? '▼' : '▲';
  };

  const totalRerolls = useMemo(
    () => report.chipEvents.reduce((acc, ev) => acc + (ev.rerollCount || 0), 0),
    [report.chipEvents],
  );
  const netEconomySum = useMemo(
    () => report.players.reduce((acc, p) => acc + (p.netEconomy || 0), 0),
    [report.players],
  );

  const roundsList = useMemo(
    () => Array.from({ length: Math.max(0, report.meta.rounds) }, (_, i) => i + 1),
    [report.meta.rounds],
  );

  const filteredTransfers = useMemo(() => {
    return report.transfers.filter((t) => {
      if (
        transferPlayerFilter !== 'all' &&
        t.fromPid !== transferPlayerFilter &&
        t.toPid !== transferPlayerFilter
      ) {
        return false;
      }
      if (transferRoundFilter !== 'all' && String(t.round) !== transferRoundFilter) {
        return false;
      }
      return true;
    });
  }, [report.transfers, transferPlayerFilter, transferRoundFilter]);

  const filteredOpportunities = useMemo(() => {
    return (report.transferSummary.opportunities || []).filter((o) => {
      if (transferPlayerFilter !== 'all' && o.pid !== transferPlayerFilter) return false;
      if (transferRoundFilter !== 'all' && String(o.round) !== transferRoundFilter) return false;
      return true;
    });
  }, [report.transferSummary.opportunities, transferPlayerFilter, transferRoundFilter]);

  const economyTotals = useMemo(() => {
    const sums = { monster: 0, transfer: 0, chip: 0, other: 0, out: 0, net: 0 };
    for (const r of report.roundEconomy) {
      for (const st of Object.values(r.players || {})) {
        sums.monster += st.monster || 0;
        sums.transfer += st.transfer || 0;
        sums.chip += st.chip || 0;
        sums.other += st.other || 0;
        sums.out += st.out || 0;
        sums.net += st.net || 0;
      }
    }
    return sums;
  }, [report.roundEconomy]);

  const scrollToSection = (id: string) => {
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  return (
    <div style={{ display: 'grid', gap: 18 }}>
      <div className="replay-kpi-grid">
        <button
          type="button"
          className="replay-kpi-card"
          onClick={() => onSwitchTab('chips')}
        >
          <strong>
            <span>筹码选择</span>
            <Cpu size={15} color="#10b981" />
          </strong>
          <span>
            {report.chipEvents.length} 次三选一 · 刷新 {totalRerolls} 次
          </span>
        </button>

        <button
          type="button"
          className="replay-kpi-card"
          onClick={() => scrollToSection('replay-transfers-section')}
        >
          <strong>
            <span>
              转账明细 <em style={{ fontStyle: 'normal', color: '#10b981' }}>{report.transferSummary.count} 笔</em>
            </span>
            <Coins size={15} color="#f59e0b" />
          </strong>
          <span>
            合计 {formatInt(report.transferSummary.totalGold)} 星币 · 未到店 {report.transferSummary.missedCount} 轮
          </span>
        </button>

        <button
          type="button"
          className="replay-kpi-card"
          onClick={() => scrollToSection('replay-economy-section')}
        >
          <strong>
            <span>逐轮经济</span>
            <TrendingUp size={15} color="#3b82f6" />
          </strong>
          <span>
            团队净经济 {formatInt(netEconomySum)} · 转化 {formatInt(report.players.reduce((a, b) => a + b.starSpend, 0))} 升星
          </span>
        </button>

        <button
          type="button"
          className="replay-kpi-card"
          onClick={() => onSwitchTab('board')}
        >
          <strong>
            <span>
              关键事件 <em style={{ fontStyle: 'normal', color: '#8b5cf6' }}>{report.steps.length} 步</em>
            </span>
            <Flag size={15} color="#8b5cf6" />
          </strong>
          <span>点击进入逐轮棋盘与实时手牌回放 ↗</span>
        </button>
      </div>

      <section aria-label="四人战斗总览">
        <div className="replay-combat-grid">
          {report.players.map((p, idx) => {
            const color = playerSeatColor(p, idx + 1);
            return (
              <article key={p.id} className="replay-player-card">
                <div className="replay-player-stripe" style={{ background: color }} />
                <div className="replay-player-head">
                  <div className="replay-avatar">
                    <GameImage asset={heroImageKey(p.heroId, `overview:${p.id}`)} name={heroDisplayName(p)} className="portrait" />
                  </div>
                  <div style={{ minWidth: 0, flex: 1 }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
                      <strong style={{ fontSize: 15, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {heroDisplayName(p)}
                      </strong>
                      <span style={{ fontSize: 12, fontWeight: 700, color }}>{p.label}</span>
                    </div>
                    <div style={{ fontSize: 12, color: 'var(--text-secondary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                      Lv.{p.level} · {p.nick}
                    </div>
                  </div>
                </div>

                <div>
                  <div className="replay-big-metric">{formatInt(p.totalDamage)}</div>
                  <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
                    输出伤害 · 有效 {formatInt(p.realDamage)}
                  </div>
                  <div className="replay-damage-breakdown" style={{ marginTop: 6 }}>
                    <span>任务怪 {formatInt(p.taskMonsterDamage)}</span>
                    <span>· Boss {formatInt(p.bossDamage)}</span>
                    {p.finalKillBoss && <span className="replay-badge gold">最终击杀 Boss</span>}
                  </div>
                </div>

                <div className="replay-mini-stats">
                  <div>
                    <span>击倒</span>
                    <strong>{formatInt(p.killCount)}</strong>
                  </div>
                  <div>
                    <span>倒下</span>
                    <strong>{formatInt(p.totalDie)}</strong>
                  </div>
                  <div>
                    <span>治疗</span>
                    <strong>{formatInt(p.treatmentScore)}</strong>
                  </div>
                </div>

                {p.relics.length > 0 && (
                  <div className="replay-card-chip-list">
                    {p.relics.slice(0, 6).map((relic) => (
                      <span
                        key={relic.id}
                        className="replay-hand-pill"
                        title={relic.desc || relic.description || relic.name}
                      >
                        {relic.name}
                      </span>
                    ))}
                    {p.relics.length > 6 && (
                      <span className="replay-hand-pill">+{p.relics.length - 6}</span>
                    )}
                  </div>
                )}
              </article>
            );
          })}
        </div>
      </section>

      <section id="replay-transfers-section" className="panel" style={{ display: 'grid', gap: 14 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
          <div>
            <h3 style={{ fontSize: 17, margin: 0 }}>转账明细</h3>
            <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', margin: '4px 0 0' }}>
              实际转账与转账机会，点击记录回看 · 共 <strong>{report.transferSummary.count}</strong> 笔 · 合计{' '}
              <strong>{formatInt(report.transferSummary.totalGold)}</strong> 星币 · 可到店但未去{' '}
              <strong>{report.transferSummary.missedCount}</strong> 轮
            </p>
          </div>
          <div className="replay-pill-tabs">
            <button
              type="button"
              className={`replay-pill-tab${transferView === 'actual' ? ' active' : ''}`}
              onClick={() => setTransferView('actual')}
            >
              实际转账 {report.transfers.length} 笔
            </button>
            <button
              type="button"
              className={`replay-pill-tab${transferView === 'opportunities' ? ' active' : ''}`}
              onClick={() => setTransferView('opportunities')}
            >
              转账机会 {report.transferSummary.opportunityCount} 轮
            </button>
          </div>
        </div>

        <div className="replay-combat-grid">
          {report.players.map((p, idx) => {
            const color = playerSeatColor(p, idx + 1);
            return (
              <div
                key={`pre6-${p.id}`}
                style={{
                  border: '1px solid var(--border-color)',
                  borderRadius: 11,
                  padding: '12px 14px',
                  background: 'var(--bg-input)',
                  display: 'grid',
                  gap: 6,
                }}
              >
                <div style={{ fontWeight: 700, color, fontSize: 13.5 }}>
                  {p.label} {heroDisplayName(p)}
                </div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary)', borderBottom: '1px solid var(--border-color)', paddingBottom: 6 }}>
                  转出 {formatInt(p.transferOut)} · 收到 {formatInt(p.transferIn)}
                </div>
                <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 8 }}>
                  <span style={{ fontSize: 12, color: 'var(--text-secondary)' }}>前6轮转账意愿</span>
                  <strong style={{ fontSize: 20 }}>{formatPercent(p.pre6Willingness)}</strong>
                </div>
                <div style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>
                  {p.pre6PayCount} 轮代付 / {p.pre6OppCount} 轮机会
                </div>
                <div style={{ fontSize: 11.5, color: p.missedShopRounds.length > 0 ? '#d97706' : 'var(--text-muted)' }}>
                  可到店但未去：{p.missedShopRounds.length > 0 ? p.missedShopRounds.map((r) => `第 ${r} 轮`).join('、') : '无'}
                </div>
              </div>
            );
          })}
        </div>

        <p style={{ fontSize: 11.5, color: 'var(--text-muted)', margin: 0 }}>
          前6轮转账意愿 = 进店代付轮数 ÷ 转账机会轮数。进店或持有星币且按掷骰可到达商店但未经过商店，分别计入对应统计。
        </p>

        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
          <label style={{ fontSize: 12.5, display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <span style={{ color: 'var(--text-secondary)' }}>相关玩家</span>
            <select
              className="replay-select"
              value={transferPlayerFilter}
              onChange={(e) => setTransferPlayerFilter(e.target.value)}
            >
              <option value="all">全部玩家</option>
              {report.players.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.label} {heroDisplayName(p)}
                </option>
              ))}
            </select>
          </label>
          <label style={{ fontSize: 12.5, display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <span style={{ color: 'var(--text-secondary)' }}>轮次</span>
            <select
              className="replay-select"
              value={transferRoundFilter}
              onChange={(e) => setTransferRoundFilter(e.target.value)}
            >
              <option value="all">全部轮次</option>
              {roundsList.map((r) => (
                <option key={r} value={String(r)}>
                  第 {r} 轮
                </option>
              ))}
            </select>
          </label>
        </div>

        {transferView === 'actual' ? (
          filteredTransfers.length === 0 ? (
            <div style={{ fontSize: 13, color: 'var(--text-secondary)', padding: '12px 0' }}>
              当前筛选条件下暂无转账记录。
            </div>
          ) : (
            <div className="replay-transfer-list">
              {filteredTransfers.map((t) => {
                const fromPlayer = report.players.find((p) => p.id === t.fromPid);
                const toPlayer = report.players.find((p) => p.id === t.toPid);
                return (
                  <div key={`${t.index}-${t.stepIndex}`} className="replay-transfer-row">
                    <div>
                      <strong style={{ display: 'block' }}>第 {t.round} 轮</strong>
                      <span style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>{t.source}</span>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <span style={{ fontWeight: 700, color: playerSeatColor(fromPlayer, 1) }}>
                        {t.fromLabel} {t.fromName}
                      </span>
                      <span style={{ color: 'var(--text-muted)' }}>→</span>
                      <span style={{ fontWeight: 700, color: playerSeatColor(toPlayer, 2) }}>
                        {t.toLabel} {t.toName}
                      </span>
                    </div>
                    <strong style={{ color: '#d97706', fontSize: 15 }}>
                      {t.amount} <small style={{ fontWeight: 500 }}>星币</small>
                    </strong>
                    <button
                      type="button"
                      className="btn btn-sm"
                      onClick={() => onJumpToStep(t.stepIndex)}
                    >
                      <span>回看</span>
                      <ArrowUpRight size={13} />
                    </button>
                  </div>
                );
              })}
            </div>
          )
        ) : filteredOpportunities.length === 0 ? (
          <div style={{ fontSize: 13, color: 'var(--text-secondary)', padding: '12px 0' }}>
            当前筛选条件下暂无转账机会统计。
          </div>
        ) : (
          <div className="replay-transfer-list">
            {filteredOpportunities.map((o, idx) => {
              const stepTarget = report.steps.find((s) => s.round === o.round && s.actorPid === o.pid)?.index;
              return (
                <div key={`${o.round}-${o.pid}-${idx}`} className="replay-transfer-row">
                  <div>
                    <strong style={{ display: 'block' }}>第 {o.round} 轮</strong>
                    <span style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>
                      骰 {o.roll} · 携 {o.gold} 星币
                    </span>
                  </div>
                  <div>
                    <strong>
                      {o.playerLabel} {o.playerName}
                    </strong>
                    <div style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>
                      可达商店格：{o.reachableShops.length > 0 ? o.reachableShops.join(', ') : o.isRemote ? '远程' : '无'}
                    </div>
                  </div>
                  <div>
                    <span
                      className={`replay-badge ${
                        o.paid ? 'ok' : o.missedShop ? 'danger' : 'info'
                      }`}
                    >
                      {o.paid ? '已代付' : o.missedShop ? '可到店但未去' : o.openedShop ? '进店未代付' : '未代付'}
                    </span>
                  </div>
                  {stepTarget != null ? (
                    <button type="button" className="btn btn-sm" onClick={() => onJumpToStep(stepTarget)}>
                      <span>回看</span>
                      <ArrowUpRight size={13} />
                    </button>
                  ) : (
                    <span />
                  )}
                </div>
              );
            })}
          </div>
        )}
      </section>

      <section className="panel" style={{ display: 'grid', gap: 12 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <ListChecks size={17} color="#10b981" />
          <h3 style={{ fontSize: 17, margin: 0 }}>地图任务与任务怪伤害</h3>
        </div>
        {report.missions.length === 0 ? (
          <p style={{ fontSize: 13, color: 'var(--text-secondary)', margin: 0 }}>本局未记录到地图任务事件。</p>
        ) : (
          <div style={{ display: 'grid', gap: 10 }}>
            {report.missions.map((m, idx) => {
              const pct = m.total > 0 ? Math.min(100, Math.round((m.progress / m.total) * 100)) : 0;
              return (
                <div
                  key={`${m.id}-${idx}`}
                  style={{
                    border: '1px solid var(--border-color)',
                    borderRadius: 11,
                    padding: '12px 14px',
                    background: 'var(--bg-input)',
                    display: 'grid',
                    gap: 8,
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
                    <strong style={{ fontSize: 14 }}>{m.title}</strong>
                    <div style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 12 }}>
                      <span style={{ color: 'var(--text-secondary)' }}>
                        第 {m.appearedRound} 轮出现 · {m.completedRound ? `第 ${m.completedRound} 轮完成` : '未完成'}
                      </span>
                      <span className={`replay-badge ${m.completedRound ? 'ok' : 'info'}`}>
                        {m.progress}/{m.total} ({pct}%)
                      </span>
                    </div>
                  </div>
                  <div style={{ height: 6, borderRadius: 999, background: 'var(--border-color)', overflow: 'hidden' }}>
                    <div
                      style={{
                        height: '100%',
                        width: `${pct}%`,
                        background: m.completedRound ? '#10b981' : '#3b82f6',
                      }}
                    />
                  </div>
                  <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', fontSize: 12 }}>
                    {report.players.map((p) => {
                      const val = m.damageByPlayer?.[p.id];
                      return (
                        <span key={p.id} style={{ color: 'var(--text-secondary)' }}>
                          <strong style={{ color: playerSeatColor(p) }}>{p.label}</strong> {heroDisplayName(p)}：
                          <strong>{val == null ? '—' : formatInt(val)}</strong>
                        </span>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </section>

      <div className="replay-trends-grid">
        <section className="panel" style={{ display: 'grid', gap: 12 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
            <h3 style={{ fontSize: 17, margin: 0 }}>逐轮走势</h3>
            <div className="replay-pill-tabs">
              {TREND_METRICS.map((opt) => (
                <button
                  key={opt.key}
                  type="button"
                  className={`replay-pill-tab${trendMetric === opt.key ? ' active' : ''}`}
                  onClick={() => setTrendMetric(opt.key)}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          </div>
          <RoundTrendsSvg report={report} metric={trendMetric} />
        </section>

        <section className="panel" style={{ display: 'grid', gap: 10, alignContent: 'start' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Sparkles size={16} color="#10b981" />
            <h3 style={{ fontSize: 17, margin: 0 }}>本局观察</h3>
          </div>
          {report.highlights.length === 0 ? (
            <p style={{ fontSize: 13, color: 'var(--text-secondary)', margin: 0 }}>暂无自动摘要。</p>
          ) : (
            <ul style={{ margin: 0, paddingLeft: 18, display: 'grid', gap: 8, fontSize: 13, lineHeight: 1.6 }}>
              {report.highlights.map((h, i) => (
                <li key={i}>{h}</li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <section id="replay-economy-section" className="panel" style={{ display: 'grid', gap: 12 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
          <div>
            <h3 style={{ fontSize: 17, margin: 0 }}>逐轮经济统计</h3>
            <p style={{ fontSize: 12, color: 'var(--text-secondary)', margin: '4px 0 0' }}>
              打怪 +{formatInt(economyTotals.monster)} · 代付收入 {formatSignedInt(economyTotals.transfer)} · 筹码 +{formatInt(economyTotals.chip)} ·
              其他 +{formatInt(economyTotals.other)} · 流出 -{formatInt(economyTotals.out)}
            </p>
          </div>
        </div>
        <RoundEconomySvg report={report} />
      </section>

      <section className="panel" style={{ display: 'grid', gap: 12 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <h3 style={{ fontSize: 17, margin: 0 }}>推进与经济汇总</h3>
          <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>点击表头可排序 · 正负经济已标明符号</span>
        </div>
        <div className="replay-table-wrap">
          <table className="replay-table">
            <thead>
              <tr>
                <th>
                  <button type="button" className="replay-sort-btn" onClick={() => toggleEcoSort('slot')}>
                    席位 <span>{ecoArrow('slot')}</span>
                  </button>
                </th>
                <th>角色</th>
                <th className="num">
                  <button type="button" className="replay-sort-btn" onClick={() => toggleEcoSort('totalDamage')}>
                    总伤害 / 有效 <span>{ecoArrow('totalDamage')}</span>
                  </button>
                </th>
                <th className="num">
                  <button type="button" className="replay-sort-btn" onClick={() => toggleEcoSort('netEconomy')}>
                    净经济 <span>{ecoArrow('netEconomy')}</span>
                  </button>
                </th>
                <th className="num">
                  <button type="button" className="replay-sort-btn" onClick={() => toggleEcoSort('totalGold')}>
                    累计星币 <span>{ecoArrow('totalGold')}</span>
                  </button>
                </th>
                <th className="num">
                  <button type="button" className="replay-sort-btn" onClick={() => toggleEcoSort('starSpend')}>
                    升星花费 <span>{ecoArrow('starSpend')}</span>
                  </button>
                </th>
                <th className="num">转出 / 收到</th>
                <th className="num">
                  <button type="button" className="replay-sort-btn" onClick={() => toggleEcoSort('conversionRatio')}>
                    转化比 <span>{ecoArrow('conversionRatio')}</span>
                  </button>
                </th>
                <th className="num">
                  <button type="button" className="replay-sort-btn" onClick={() => toggleEcoSort('movePoint')}>
                    行动点 <span>{ecoArrow('movePoint')}</span>
                  </button>
                </th>
                <th className="num">
                  <button type="button" className="replay-sort-btn" onClick={() => toggleEcoSort('pre6Willingness')}>
                    前6轮意愿 <span>{ecoArrow('pre6Willingness')}</span>
                  </button>
                </th>
              </tr>
            </thead>
            <tbody>
              {sortedEconomyPlayers.map((p) => (
                <tr key={`row-${p.id}`}>
                  <td>
                    <strong style={{ color: playerSeatColor(p) }}>{p.label}</strong>
                  </td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                      <strong>{heroDisplayName(p)}</strong>
                      {topDamage > 0 && p.totalDamage === topDamage && (
                        <span className="replay-badge gold">MVP 输出</span>
                      )}
                    </div>
                    <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>{p.nick}</div>
                  </td>
                  <td className="num">
                    <strong>{formatInt(p.totalDamage)}</strong> /{' '}
                    <span style={{ color: 'var(--text-secondary)' }}>{formatInt(p.realDamage)}</span>
                  </td>
                  <td className="num">
                    <strong className={p.netEconomy > 0 ? 'eco-pos' : p.netEconomy < 0 ? 'eco-neg' : undefined}>
                      {formatSignedInt(p.netEconomy)}
                    </strong>
                  </td>
                  <td className="num">{formatInt(p.totalGold)}</td>
                  <td className="num">{formatInt(p.starSpend)}</td>
                  <td className="num">
                    {formatInt(p.transferOut)} / {formatInt(p.transferIn)}
                  </td>
                  <td className="num">{formatRatio(p.conversionRatio)}</td>
                  <td className="num">{formatInt(p.movePoint)}</td>
                  <td className="num">{formatPercent(p.pre6Willingness)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
};

const ReplayPlayerLegend: React.FC<{
  players: ReplayAnalysisReport['players'];
  activePlayerId: string | null;
  onSelect: (pid: string | null) => void;
}> = ({ players, activePlayerId, onSelect }) => (
  <div className="replay-legend-row" role="group" aria-label="按角色高亮或筛选图表">
    {players.map((p) => {
      const selected = activePlayerId === p.id;
      const dimmed = activePlayerId !== null && !selected;
      return (
        <button
          key={p.id}
          type="button"
          className={`replay-legend-btn${selected ? ' is-selected' : ''}${dimmed ? ' is-dimmed' : ''}`}
          aria-pressed={selected}
          onClick={() => onSelect(selected ? null : p.id)}
          title={selected ? '再次点击恢复全部角色' : '点击突出显示该角色'}
        >
          <span className="replay-legend-dot" style={{ background: playerSeatColor(p) }} />
          <span>
            {p.label} {heroDisplayName(p)}
          </span>
        </button>
      );
    })}
    {activePlayerId !== null && (
      <button type="button" className="replay-legend-reset" onClick={() => onSelect(null)}>
        显示全部
      </button>
    )}
  </div>
);

const RoundTrendsSvg: React.FC<{ report: ReplayAnalysisReport; metric: TrendMetric }> = ({
  report,
  metric,
}) => {
  const rows = report.roundTrends || [];
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null);
  const [focusedPid, setFocusedPid] = useState<string | null>(null);

  if (rows.length === 0) {
    return <p style={{ fontSize: 13, color: 'var(--text-secondary)' }}>暂无逐轮数据。</p>;
  }

  let maxVal = 1;
  for (const r of rows) {
    for (const p of report.players) {
      const val = r.players?.[p.id]?.[metric] ?? 0;
      if (val > maxVal) maxVal = val;
    }
  }

  const w = 680;
  const h = 236;
  const padL = 44;
  const padR = 18;
  const padT = 18;
  const padB = 30;
  const innerW = w - padL - padR;
  const innerH = h - padT - padB;
  const xFor = (idx: number) =>
    rows.length <= 1 ? padL + innerW / 2 : padL + (idx / (rows.length - 1)) * innerW;
  const yFor = (val: number) => padT + innerH - (Math.max(0, val) / maxVal) * innerH;
  const colHalf = rows.length <= 1 ? innerW / 2 : innerW / Math.max(1, rows.length - 1) / 2;

  const activeRow = hoveredIdx != null ? rows[hoveredIdx] : null;
  const activeX = hoveredIdx != null ? xFor(hoveredIdx) : 0;
  const orderedPlayers = [...report.players].sort((a, b) => {
    if (a.id === focusedPid) return 1;
    if (b.id === focusedPid) return -1;
    return a.slot - b.slot;
  });

  return (
    <div style={{ display: 'grid', gap: 10 }}>
      <div className="replay-chart-stage" onMouseLeave={() => setHoveredIdx(null)}>
        <svg viewBox={`0 0 ${w} ${h}`} style={{ width: '100%', height: 'auto', display: 'block' }}>
          {[0, 0.25, 0.5, 0.75, 1].map((t) => {
            const y = padT + innerH * (1 - t);
            return (
              <g key={t}>
                <line x1={padL} y1={y} x2={w - padR} y2={y} stroke="var(--border-color)" strokeDasharray="3 3" />
                <text x={padL - 6} y={y + 4} textAnchor="end" fontSize="10" fill="var(--text-muted)">
                  {Math.round(maxVal * t)}
                </text>
              </g>
            );
          })}

          {activeRow && (
            <line
              x1={activeX}
              y1={padT}
              x2={activeX}
              y2={padT + innerH}
              stroke="rgba(16, 185, 129, 0.65)"
              strokeWidth="1.5"
              strokeDasharray="4 3"
            />
          )}

          {rows.map((r, i) => (
            <text
              key={r.round}
              x={xFor(i)}
              y={h - 8}
              textAnchor="middle"
              fontSize="10"
              fontWeight={hoveredIdx === i ? 700 : 400}
              fill={hoveredIdx === i ? '#10b981' : 'var(--text-secondary)'}
            >
              R{r.round}
            </text>
          ))}

          {orderedPlayers.map((p) => {
            const color = playerSeatColor(p);
            const dimmed = focusedPid !== null && focusedPid !== p.id;
            const pts = rows.map((r, i) => {
              const v = r.players?.[p.id]?.[metric] ?? 0;
              return { x: xFor(i), y: yFor(v), v, round: r.round };
            });
            const poly = pts.map((pt) => `${pt.x},${pt.y}`).join(' ');
            return (
              <g key={p.id} style={{ opacity: dimmed ? 0.18 : 1, transition: 'opacity 0.18s ease' }}>
                <polyline
                  className="replay-trend-line"
                  fill="none"
                  stroke={color}
                  strokeWidth={focusedPid === p.id ? '3.1' : '2.4'}
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  points={poly}
                />
                {pts.map((pt, idx) => (
                  <circle
                    key={`${p.id}-${pt.round}`}
                    cx={pt.x}
                    cy={pt.y}
                    r={hoveredIdx === idx ? '5.2' : focusedPid === p.id ? '4.1' : '3.3'}
                    fill={color}
                    stroke="#ffffff"
                    strokeWidth={hoveredIdx === idx ? '1.5' : '0.8'}
                  />
                ))}
              </g>
            );
          })}

          {rows.map((r, i) => {
            const cx = xFor(i);
            const rx = Math.max(padL, cx - colHalf);
            const rw = Math.min(w - padR, cx + colHalf) - rx;
            return (
              <rect
                key={`hit-${r.round}`}
                x={rx}
                y={padT}
                width={Math.max(8, rw)}
                height={innerH + 18}
                fill="transparent"
                style={{ cursor: 'crosshair' }}
                onMouseEnter={() => setHoveredIdx(i)}
                onClick={() => setHoveredIdx((prev) => (prev === i ? null : i))}
              />
            );
          })}
        </svg>

        {activeRow && (
          <div
            className="replay-chart-tooltip"
            style={{
              left: `${Math.min(72, Math.max(8, (activeX / w) * 100))}%`,
            }}
          >
            <strong>第 {activeRow.round} 轮 · {TREND_METRICS.find((m) => m.key === metric)?.label}</strong>
            {(focusedPid ? report.players.filter((p) => p.id === focusedPid) : [...report.players])
              .sort((a, b) => (activeRow.players?.[b.id]?.[metric] ?? 0) - (activeRow.players?.[a.id]?.[metric] ?? 0))
              .map((p) => (
                <div key={p.id} className="replay-chart-tooltip-row">
                  <span>
                    <i style={{ background: playerSeatColor(p) }} />
                    {p.label} {heroDisplayName(p)}
                  </span>
                  <strong>{formatInt(activeRow.players?.[p.id]?.[metric] ?? 0)}</strong>
                </div>
              ))}
          </div>
        )}
      </div>

      <ReplayPlayerLegend players={report.players} activePlayerId={focusedPid} onSelect={setFocusedPid} />
    </div>
  );
};

const RoundEconomySvg: React.FC<{ report: ReplayAnalysisReport }> = ({ report }) => {
  const rows = report.roundEconomy || [];
  const [hoveredIdx, setHoveredIdx] = useState<number | null>(null);
  const [focusedPid, setFocusedPid] = useState<string | null>(null);

  if (rows.length === 0) {
    return <p style={{ fontSize: 13, color: 'var(--text-secondary)' }}>暂无逐轮经济统计。</p>;
  }

  let maxNet = 10;
  let minNet = 0;
  for (const r of rows) {
    for (const p of report.players) {
      const v = r.players?.[p.id]?.net ?? 0;
      if (v > maxNet) maxNet = v;
      if (v < minNet) minNet = v;
    }
  }
  const span = Math.max(1, maxNet - minNet);

  const w = 880;
  const h = 238;
  const padL = 44;
  const padR = 16;
  const padT = 18;
  const padB = 30;
  const innerW = w - padL - padR;
  const innerH = h - padT - padB;
  const zeroY = padT + (maxNet / span) * innerH;
  const groupW = innerW / rows.length;
  const barW = Math.max(4, Math.min(16, (groupW * 0.78) / Math.max(1, report.players.length)));
  const activeRow = hoveredIdx != null ? rows[hoveredIdx] : null;
  const activeCx = hoveredIdx != null ? padL + hoveredIdx * groupW + groupW / 2 : 0;

  return (
    <div style={{ display: 'grid', gap: 10 }}>
      <div className="replay-chart-stage" style={{ overflowX: 'auto' }} onMouseLeave={() => setHoveredIdx(null)}>
        <svg viewBox={`0 0 ${w} ${h}`} style={{ width: '100%', minWidth: 520, height: 'auto', display: 'block' }}>
          <line x1={padL} y1={zeroY} x2={w - padR} y2={zeroY} stroke="var(--text-muted)" strokeWidth="1" />
          <text x={padL - 6} y={padT + 8} textAnchor="end" fontSize="10" fill="var(--text-muted)">
            +{Math.round(maxNet)}
          </text>
          <text x={padL - 6} y={zeroY + 4} textAnchor="end" fontSize="10" fill="var(--text-muted)">
            0
          </text>
          {minNet < 0 && (
            <text x={padL - 6} y={padT + innerH} textAnchor="end" fontSize="10" fill="var(--text-muted)">
              {Math.round(minNet)}
            </text>
          )}

          {activeRow && (
            <rect
              x={padL + hoveredIdx! * groupW + 2}
              y={padT}
              width={Math.max(4, groupW - 4)}
              height={innerH}
              rx="6"
              fill="rgba(59, 130, 246, 0.08)"
              stroke="rgba(59, 130, 246, 0.35)"
              strokeDasharray="3 3"
            />
          )}

          {rows.map((r, ri) => {
            const cx = padL + ri * groupW + groupW / 2;
            const totalBarsW = report.players.length * barW + (report.players.length - 1) * 2;
            const startX = cx - totalBarsW / 2;
            return (
              <g key={r.round}>
                {report.players.map((p, pi) => {
                  const st = r.players?.[p.id];
                  const net = st?.net ?? 0;
                  const yVal = padT + ((maxNet - net) / span) * innerH;
                  const y = Math.min(zeroY, yVal);
                  const bh = Math.max(2, Math.abs(zeroY - yVal));
                  const dimmed = focusedPid !== null && focusedPid !== p.id;
                  return (
                    <rect
                      key={`${r.round}-${p.id}`}
                      className="replay-eco-bar"
                      x={startX + pi * (barW + 2)}
                      y={y}
                      width={barW}
                      height={bh}
                      rx="2"
                      fill={playerSeatColor(p)}
                      opacity={dimmed ? 0.18 : hoveredIdx === ri ? 1 : 0.88}
                      style={{ transition: 'opacity 0.16s ease' }}
                    />
                  );
                })}
                <text
                  x={cx}
                  y={h - 8}
                  textAnchor="middle"
                  fontSize="10"
                  fontWeight={hoveredIdx === ri ? 700 : 400}
                  fill={hoveredIdx === ri ? '#3b82f6' : 'var(--text-secondary)'}
                >
                  R{r.round}
                </text>
                <rect
                  x={padL + ri * groupW}
                  y={padT}
                  width={groupW}
                  height={innerH + 18}
                  fill="transparent"
                  style={{ cursor: 'crosshair' }}
                  onMouseEnter={() => setHoveredIdx(ri)}
                  onClick={() => setHoveredIdx((prev) => (prev === ri ? null : ri))}
                />
              </g>
            );
          })}
        </svg>

        {activeRow && (
          <div
            className="replay-chart-tooltip"
            style={{
              left: `${Math.min(72, Math.max(8, (activeCx / w) * 100))}%`,
            }}
          >
            <strong>{activeRow.label || `第 ${activeRow.round} 轮`} · 净经济</strong>
            {(focusedPid ? report.players.filter((p) => p.id === focusedPid) : report.players).map((p) => {
              const st = activeRow.players?.[p.id];
              const net = st?.net ?? 0;
              return (
                <div key={p.id} className="replay-chart-tooltip-row">
                  <span>
                    <i style={{ background: playerSeatColor(p) }} />
                    {p.label} {heroDisplayName(p)}
                  </span>
                  <strong className={net > 0 ? 'eco-pos' : net < 0 ? 'eco-neg' : undefined}>
                    {formatSignedInt(net)}
                  </strong>
                </div>
              );
            })}
          </div>
        )}
      </div>

      <ReplayPlayerLegend players={report.players} activePlayerId={focusedPid} onSelect={setFocusedPid} />
    </div>
  );
};
