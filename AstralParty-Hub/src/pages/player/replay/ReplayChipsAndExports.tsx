import React, { useMemo, useState } from 'react';
import { ArrowUpRight, Download, FileCode2, FileJson, Image as ImageIcon, Printer } from 'lucide-react';
import type { ReplayAnalysisReport } from '../../../shared/webCloudClient';
import {
  chipQualityTone,
  formatDuration,
  formatInt,
  formatPercent,
  formatSignedInt,
  heroDisplayName,
  lookupChip,
  playerSeatColor,
} from './replayShared';

function escapeHtml(text: unknown): string {
  return String(text ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function triggerDownload(filename: string, content: BlobPart, mime: string) {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function downloadChipPosterPng(report: ReplayAnalysisReport) {
  const width = 1080;
  const rows = Math.max(1, report.chipEvents.length);
  const height = Math.max(520, 260 + report.players.length * 90 + Math.min(24, rows) * 44);
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  ctx.fillStyle = '#0f172a';
  ctx.fillRect(0, 0, width, height);

  ctx.fillStyle = '#10b981';
  ctx.font = 'bold 16px system-ui, sans-serif';
  ctx.fillText(`XINGSHU HUB · REPLAY #${report.replayId}`, 44, 48);

  ctx.fillStyle = '#f8fafc';
  ctx.font = 'bold 30px system-ui, sans-serif';
  ctx.fillText(`${report.meta.mapName} · ${report.meta.difficultyName}`, 44, 88);

  ctx.fillStyle = '#94a3b8';
  ctx.font = '16px system-ui, sans-serif';
  ctx.fillText(
    `${report.meta.rounds} 轮 · ${formatDuration(report.meta.durationSec)} · ${report.meta.win ? '通关胜利' : '未能通关'} · ${report.meta.version}`,
    44,
    120,
  );

  let y = 160;
  for (const [idx, p] of report.players.entries()) {
    ctx.fillStyle = 'rgba(30, 41, 59, 0.9)';
    ctx.fillRect(44, y, width - 88, 72);
    ctx.fillStyle = playerSeatColor(p, idx + 1);
    ctx.fillRect(44, y, 6, 72);

    ctx.fillStyle = '#f8fafc';
    ctx.font = 'bold 18px system-ui, sans-serif';
    ctx.fillText(`${p.label} ${heroDisplayName(p)} · ${p.nick}`, 64, y + 28);

    ctx.fillStyle = '#cbd5e1';
    ctx.font = '14px system-ui, sans-serif';
    const relicsText = p.relics.map((r) => r.name).join(' / ') || '无最终筹码记录';
    ctx.fillText(
      `总伤害 ${formatInt(p.totalDamage)} · 净经济 ${formatInt(p.netEconomy)} · 筹码：${relicsText.slice(0, 56)}`,
      64,
      y + 54,
    );
    y += 84;
  }

  y += 18;
  ctx.fillStyle = '#10b981';
  ctx.font = 'bold 18px system-ui, sans-serif';
  ctx.fillText(`筹码三选一记录（共 ${report.chipEvents.length} 次）`, 44, y);
  y += 28;

  for (const ev of report.chipEvents.slice(0, 24)) {
    const chosen = lookupChip(ev.chosenId, ev.chosen);
    ctx.fillStyle = '#1e293b';
    ctx.fillRect(44, y, width - 88, 34);
    ctx.fillStyle = '#94a3b8';
    ctx.font = '13px system-ui, sans-serif';
    ctx.fillText(`R${ev.round} · ${ev.playerLabel} ${ev.playerName}`, 58, y + 22);
    ctx.fillStyle = '#f8fafc';
    ctx.font = 'bold 14px system-ui, sans-serif';
    ctx.fillText(`选择：${chosen.name}`, 330, y + 22);
    ctx.fillStyle = '#38bdf8';
    ctx.font = '13px system-ui, sans-serif';
    ctx.fillText(`${ev.source} · 刷新 ${ev.rerollCount} 次`, 700, y + 22);
    y += 42;
  }

  canvas.toBlob((blob) => {
    if (!blob) return;
    triggerDownload(`xingshu-chips-${report.replayId}.png`, blob, 'image/png');
  }, 'image/png');
}

function buildStandaloneHtmlReport(report: ReplayAnalysisReport): string {
  const rowsHtml = report.players
    .map(
      (p) => `<tr>
      <td>${escapeHtml(p.label)}</td>
      <td>${escapeHtml(heroDisplayName(p))} (${escapeHtml(p.nick)})</td>
      <td>${formatInt(p.totalDamage)}</td>
      <td>${formatInt(p.realDamage)}</td>
      <td>${formatInt(p.bossDamage)}</td>
      <td>${formatInt(p.killCount)}/${formatInt(p.totalDie)}/${formatInt(p.treatmentScore)}</td>
      <td>${formatInt(p.netEconomy)}</td>
      <td>${formatPercent(p.pre6Willingness)}</td>
    </tr>`,
    )
    .join('');

  const chipsHtml = report.chipEvents
    .map((ev) => {
      const c = lookupChip(ev.chosenId, ev.chosen);
      return `<li>R${ev.round} · ${escapeHtml(ev.playerLabel)} ${escapeHtml(ev.playerName)} · <strong>${escapeHtml(c.name)}</strong> (${escapeHtml(ev.source)}，刷新 ${ev.rerollCount} 次)</li>`;
    })
    .join('');

  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>星枢战报 #${escapeHtml(report.replayId)}</title>
  <style>body{font-family:system-ui,-apple-system,sans-serif;max-width:980px;margin:28px auto;padding:0 18px;color:#0f172a;line-height:1.6}
  h1,h2{margin:0 0 10px}table{width:100%;border-collapse:collapse;margin:14px 0}th,td{border:1px solid #cbd5e1;padding:8px 10px;text-align:left;font-size:13px}th{background:#f1f5f9}</style></head>
  <body><h1>${escapeHtml(report.meta.mapName)} · ${escapeHtml(report.meta.difficultyName)}</h1>
  <p>对局编号：${escapeHtml(report.replayId)} · ${report.meta.rounds} 轮 · ${escapeHtml(formatDuration(report.meta.durationSec))} · ${report.meta.win ? '胜利' : '失败'} · 版本 ${escapeHtml(report.meta.version)}</p>
  <h2>玩家汇总</h2>
  <table><thead><tr><th>席位</th><th>角色 / 玩家</th><th>总伤害</th><th>有效</th><th>Boss伤害</th><th>击倒/倒下/治疗</th><th>净经济</th><th>前6轮意愿</th></tr></thead><tbody>${rowsHtml}</tbody></table>
  <h2>本局观察</h2><ul>${report.highlights.map((h) => `<li>${escapeHtml(h)}</li>`).join('') || '<li>无</li>'}</ul>
  <h2>筹码选择 (${report.chipEvents.length} 次)</h2><ul>${chipsHtml || '<li>无筹码记录</li>'}</ul>
  </body></html>`;
}

export const ReplayChipsTab: React.FC<{
  report: ReplayAnalysisReport;
  onJumpToStep: (stepIndex: number) => void;
}> = ({ report, onJumpToStep }) => {
  const [playerFilter, setPlayerFilter] = useState<string>('all');

  const events = useMemo(() => {
    if (playerFilter === 'all') return report.chipEvents;
    return report.chipEvents.filter((e) => e.pid === playerFilter);
  }, [report.chipEvents, playerFilter]);

  const totalRerolls = useMemo(
    () => events.reduce((acc, e) => acc + (e.rerollCount || 0), 0),
    [events],
  );

  return (
    <div style={{ display: 'grid', gap: 16 }}>
      <section className="panel" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <div>
          <h3 style={{ fontSize: 17, margin: 0 }}>筹码选择与刷新记录</h3>
          <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', margin: '4px 0 0' }}>
            共 {events.length} 次选择 · 合计刷新 {totalRerolls} 次
          </p>
        </div>

        <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
          <div className="replay-pill-tabs">
            <button
              type="button"
              className={`replay-pill-tab${playerFilter === 'all' ? ' active' : ''}`}
              onClick={() => setPlayerFilter('all')}
            >
              全部玩家
            </button>
            {report.players.map((p) => (
              <button
                key={p.id}
                type="button"
                className={`replay-pill-tab${playerFilter === p.id ? ' active' : ''}`}
                onClick={() => setPlayerFilter(p.id)}
              >
                {p.label} {heroDisplayName(p)}
              </button>
            ))}
          </div>

          <button
            type="button"
            className="btn btn-sm"
            onClick={() => downloadChipPosterPng(report)}
          >
            <ImageIcon size={14} />
            <span>导出筹码长图 (.png)</span>
          </button>
        </div>
      </section>

      {events.length === 0 ? (
        <section className="panel">
          <p style={{ color: 'var(--text-secondary)', fontSize: 13 }}>当前筛选条件下无筹码选择事件。</p>
        </section>
      ) : (
        <div style={{ display: 'grid', gap: 12 }}>
          {events.map((ev, idx) => {
            const pMeta = report.players.find((p) => p.id === ev.pid);
            const chosen = lookupChip(ev.chosenId, ev.chosen);
            const jumpStep = report.steps.find(
              (s) => s.round === ev.round && s.actorPid === ev.pid && s.kind === 'chip',
            )?.index;
            return (
              <section key={`${ev.round}-${ev.pid}-${ev.order}-${idx}`} className="panel" style={{ display: 'grid', gap: 12 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                    <span className="replay-badge info">第 {ev.round} 轮</span>
                    <strong style={{ color: playerSeatColor(pMeta, 1) }}>
                      {ev.playerLabel} {ev.playerName}
                    </strong>
                    <span style={{ fontSize: 12.5, color: 'var(--text-secondary)' }}>
                      来源：{ev.source} · 第 {ev.order} 次 · 刷新 {ev.rerollCount} 次
                    </span>
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span className={`replay-badge ${chipQualityTone(chosen.quality)}`}>
                      已选：{chosen.name}
                    </span>
                    {jumpStep != null && (
                      <button type="button" className="btn btn-sm" onClick={() => onJumpToStep(jumpStep)}>
                        <span>定位回放步</span>
                        <ArrowUpRight size={13} />
                      </button>
                    )}
                  </div>
                </div>

                <div style={{ display: 'grid', gap: 10 }}>
                  {(ev.pools || []).map((pool, pi) => (
                    <div key={pi} style={{ display: 'grid', gap: 6 }}>
                      <div style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                        {pi === 0 ? '初始三选一' : `第 ${pi} 次刷新候选`}
                      </div>
                      <div className="replay-chip-pool">
                        {pool.map((raw, ci) => {
                          const chip = lookupChip(raw.id, raw);
                          const isChosen =
                            pi === ev.pools.length - 1 && chip.id === ev.chosenId;
                          return (
                            <div
                              key={`${chip.id}-${ci}`}
                              className={`replay-chip-option${isChosen ? ' chosen' : ''}`}
                            >
                              <div style={{ display: 'flex', justifyContent: 'space-between', gap: 6, alignItems: 'center' }}>
                                <strong>{chip.name}</strong>
                                {isChosen && <span className="replay-badge ok">选中</span>}
                              </div>
                              {(chip.desc || chip.description) && (
                                <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 4 }}>
                                  {chip.desc || chip.description}
                                </div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  ))}
                </div>
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
};

type ReportSortKey =
  | 'slot'
  | 'totalDamage'
  | 'realDamage'
  | 'bossDamage'
  | 'killCount'
  | 'netEconomy'
  | 'starSpend'
  | 'pre6Willingness';

export const ReplayFullReportTab: React.FC<{ report: ReplayAnalysisReport }> = ({ report }) => {
  const [sortKey, setSortKey] = useState<ReportSortKey>('slot');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');

  const topDamage = useMemo(
    () => Math.max(0, ...report.players.map((p) => p.totalDamage || 0)),
    [report.players],
  );

  const sortedPlayers = useMemo(() => {
    const list = [...report.players];
    list.sort((a, b) => {
      const av = (a[sortKey] ?? -1) as number;
      const bv = (b[sortKey] ?? -1) as number;
      if (av === bv) return a.slot - b.slot;
      return sortDir === 'asc' ? av - bv : bv - av;
    });
    return list;
  }, [report.players, sortKey, sortDir]);

  const toggleSort = (key: ReportSortKey) => {
    if (sortKey === key) {
      setSortDir((d) => (d === 'desc' ? 'asc' : 'desc'));
    } else {
      setSortKey(key);
      setSortDir(key === 'slot' ? 'asc' : 'desc');
    }
  };

  const sortArrow = (key: ReportSortKey) => {
    if (sortKey !== key) return '↕';
    return sortDir === 'desc' ? '▼' : '▲';
  };

  return (
    <section className="panel replay-print-report" style={{ display: 'grid', gap: 18 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <div>
          <h3 style={{ fontSize: 19, margin: 0 }}>
            {report.meta.mapName} · {report.meta.difficultyName} 完整复盘报告
          </h3>
          <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', margin: '4px 0 0' }}>
            对局编号 {report.replayId} · {report.meta.rounds} 轮 · {formatDuration(report.meta.durationSec)} ·{' '}
            {report.meta.win ? '通关成功' : '未能通关'} · {report.meta.roomMode} · {report.meta.version}
          </p>
        </div>
        <button type="button" className="btn btn-primary btn-sm no-print" onClick={() => window.print()}>
          <Printer size={14} />
          <span>打印 / 另存 PDF</span>
        </button>
      </div>

      <div className="replay-table-wrap">
        <table className="replay-table">
          <thead>
            <tr>
              <th>
                <button type="button" className="replay-sort-btn" onClick={() => toggleSort('slot')}>
                  席位 <span>{sortArrow('slot')}</span>
                </button>
              </th>
              <th>角色 / 玩家</th>
              <th className="num">
                <button type="button" className="replay-sort-btn" onClick={() => toggleSort('totalDamage')}>
                  总伤害 / 有效 <span>{sortArrow('totalDamage')}</span>
                </button>
              </th>
              <th className="num">
                <button type="button" className="replay-sort-btn" onClick={() => toggleSort('bossDamage')}>
                  任务怪 / Boss <span>{sortArrow('bossDamage')}</span>
                </button>
              </th>
              <th className="num">
                <button type="button" className="replay-sort-btn" onClick={() => toggleSort('killCount')}>
                  击倒/倒下/治疗 <span>{sortArrow('killCount')}</span>
                </button>
              </th>
              <th className="num">
                <button type="button" className="replay-sort-btn" onClick={() => toggleSort('netEconomy')}>
                  净经济 / 升星 <span>{sortArrow('netEconomy')}</span>
                </button>
              </th>
              <th className="num">
                <button type="button" className="replay-sort-btn" onClick={() => toggleSort('pre6Willingness')}>
                  前6轮代付意愿 <span>{sortArrow('pre6Willingness')}</span>
                </button>
              </th>
              <th>持有筹码</th>
            </tr>
          </thead>
          <tbody>
            {sortedPlayers.map((p) => (
              <tr key={p.id}>
                <td>
                  <strong style={{ color: playerSeatColor(p) }}>{p.label}</strong>
                </td>
                <td>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                    <strong>{heroDisplayName(p)}</strong>
                    <span style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>({p.nick})</span>
                    {topDamage > 0 && p.totalDamage === topDamage && (
                      <span className="replay-badge gold">输出最高</span>
                    )}
                    {p.finalKillBoss && <span className="replay-badge ok">尾刀 Boss</span>}
                  </div>
                </td>
                <td className="num">
                  <strong>{formatInt(p.totalDamage)}</strong> / {formatInt(p.realDamage)}
                </td>
                <td className="num">
                  {formatInt(p.taskMonsterDamage)} / {formatInt(p.bossDamage)}
                </td>
                <td className="num">
                  {formatInt(p.killCount)} / {formatInt(p.totalDie)} / {formatInt(p.treatmentScore)}
                </td>
                <td className="num">
                  <strong className={p.netEconomy > 0 ? 'eco-pos' : p.netEconomy < 0 ? 'eco-neg' : undefined}>
                    {formatSignedInt(p.netEconomy)}
                  </strong>{' '}
                  / {formatInt(p.starSpend)}
                </td>
                <td className="num">{formatPercent(p.pre6Willingness)}</td>
                <td style={{ fontSize: 11.5, minWidth: 160 }}>{p.relics.map((r) => r.name).join(' · ') || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 14 }}>
        <div>
          <h4 style={{ fontSize: 14.5, margin: '0 0 8px' }}>关键结论</h4>
          <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13, lineHeight: 1.7 }}>
            {report.highlights.map((h, i) => (
              <li key={i}>{h}</li>
            ))}
          </ul>
        </div>
        <div>
          <h4 style={{ fontSize: 14.5, margin: '0 0 8px' }}>任务完成清单</h4>
          <ul style={{ margin: 0, paddingLeft: 18, fontSize: 13, lineHeight: 1.7 }}>
            {report.missions.map((m, i) => (
              <li key={i}>
                {m.title} — {m.progress}/{m.total}（第 {m.appearedRound} 轮出现
                {m.completedRound ? `，第 ${m.completedRound} 轮完成` : '，未完成'}）
              </li>
            ))}
            {report.missions.length === 0 && <li>无任务记录</li>}
          </ul>
        </div>
      </div>
    </section>
  );
};

export const ReplayExportsTab: React.FC<{ report: ReplayAnalysisReport }> = ({ report }) => {
  return (
    <section className="panel" style={{ display: 'grid', gap: 14 }}>
      <div>
        <h3 style={{ fontSize: 17, margin: 0 }}>导出战报</h3>
        <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', margin: '4px 0 0' }}>
          全部战报均在浏览器本地即时生成，无需额外上传。
        </p>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }}>
        <button
          type="button"
          className="panel"
          style={{ textAlign: 'left', cursor: 'pointer', display: 'grid', gap: 8 }}
          onClick={() =>
            triggerDownload(
              `xingshu-report-${report.replayId}.html`,
              buildStandaloneHtmlReport(report),
              'text/html;charset=utf-8',
            )
          }
        >
          <strong style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <FileCode2 size={16} color="#10b981" />
            <span>导出独立 HTML 战报</span>
          </strong>
          <span style={{ fontSize: 12.5, color: 'var(--text-secondary)' }}>
            含玩家战斗、经济表、本局观察与筹码记录，适合发群存档。
          </span>
          <span className="replay-link-btn">
            <Download size={13} /> 导出 .html
          </span>
        </button>

        <button
          type="button"
          className="panel"
          style={{ textAlign: 'left', cursor: 'pointer', display: 'grid', gap: 8 }}
          onClick={() =>
            triggerDownload(
              `xingshu-analysis-${report.replayId}.json`,
              JSON.stringify(report, null, 2),
              'application/json;charset=utf-8',
            )
          }
        >
          <strong style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <FileJson size={16} color="#3b82f6" />
            <span>导出完整结构化 JSON</span>
          </strong>
          <span style={{ fontSize: 12.5, color: 'var(--text-secondary)' }}>
            含全部 {report.steps.length} 步棋盘帧、手牌、转账与任务数据，可用于二次统计。
          </span>
          <span className="replay-link-btn">
            <Download size={13} /> 导出 .json
          </span>
        </button>

        <button
          type="button"
          className="panel"
          style={{ textAlign: 'left', cursor: 'pointer', display: 'grid', gap: 8 }}
          onClick={() => downloadChipPosterPng(report)}
        >
          <strong style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <ImageIcon size={16} color="#8b5cf6" />
            <span>生成筹码总结海报 PNG</span>
          </strong>
          <span style={{ fontSize: 12.5, color: 'var(--text-secondary)' }}>
            1080px 清晰暗色卡片长图，包含 4 名角色战绩与全流程三选一筹码列表。
          </span>
          <span className="replay-link-btn">
            <Download size={13} /> 导出 .png
          </span>
        </button>
      </div>
    </section>
  );
};
