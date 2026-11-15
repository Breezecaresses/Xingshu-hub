import React, { useEffect, useMemo, useState } from 'react';
import {
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Pause,
  Play,
  Shield,
  Skull,
  Swords,
} from 'lucide-react';
import type { ReplayAnalysisReport, ReplayStep } from '../../../shared/webCloudClient';
import { GameImage } from '../LibraryUI';
import {
  heroDisplayName,
  heroImageKey,
  lookupCard,
  lookupChip,
  playerSeatColor,
  stepKindMeta,
} from './replayShared';
import { ReplayBoardCanvas } from './ReplayBoardCanvas';

export interface ReplayBoardViewProps {
  report: ReplayAnalysisReport;
  stepIndex: number;
  onSelectStep: (idx: number) => void;
}

export const ReplayBoardView: React.FC<ReplayBoardViewProps> = ({
  report,
  stepIndex,
  onSelectStep,
}) => {
  const steps = report.steps || [];
  const safeIndex = Math.max(0, Math.min(steps.length - 1, stepIndex));
  const current: ReplayStep | undefined = steps[safeIndex];
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState<1 | 2 | 4>(1);
  const [kindFilter, setKindFilter] = useState<string>('all');

  useEffect(() => {
    if (!playing || steps.length <= 1) return;
    if (safeIndex >= steps.length - 1) {
      setPlaying(false);
      return;
    }
    const ms = speed === 4 ? 220 : speed === 2 ? 450 : 900;
    const id = window.setTimeout(() => {
      onSelectStep(Math.min(steps.length - 1, safeIndex + 1));
    }, ms);
    return () => window.clearTimeout(id);
  }, [playing, speed, safeIndex, steps.length, onSelectStep]);

  const roundFirstIndex = useMemo(() => {
    const map = new Map<number, number>();
    for (const st of steps) {
      if (!map.has(st.round)) map.set(st.round, st.index);
    }
    return map;
  }, [steps]);

  const rounds = useMemo(() => Array.from(roundFirstIndex.keys()).sort((a, b) => a - b), [roundFirstIndex]);

  const playerMetaMap = useMemo(() => {
    const map = new Map<string, (typeof report.players)[number]>();
    for (const p of report.players) map.set(p.id, p);
    return map;
  }, [report.players]);

  const filteredSteps = useMemo(() => {
    if (kindFilter === 'all') return steps;
    if (kindFilter === 'economy') {
      return steps.filter((s) => s.kind === 'shop' || s.kind === 'transfer' || s.kind === 'star');
    }
    return steps.filter((s) => s.kind === kindFilter);
  }, [steps, kindFilter]);

  const jumpRound = (delta: number) => {
    setPlaying(false);
    if (!current || rounds.length === 0) return;
    const pos = rounds.indexOf(current.round);
    const targetRound = rounds[Math.max(0, Math.min(rounds.length - 1, pos + delta))];
    const idx = roundFirstIndex.get(targetRound);
    if (idx != null) onSelectStep(idx);
  };

  if (steps.length === 0 || !current) {
    return (
      <section className="panel">
        <p style={{ color: 'var(--text-secondary)', fontSize: 13.5 }}>
          该回放未解析出逐轮行动序列。
        </p>
      </section>
    );
  }

  const currentMeta = stepKindMeta(current.kind);

  return (
    <div style={{ display: 'grid', gap: 16 }}>
      <section className="panel" style={{ display: 'grid', gap: 12 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <button type="button" className="btn btn-sm" onClick={() => jumpRound(-1)} title="上一轮">
              <ChevronsLeft size={14} />
              <span>上一轮</span>
            </button>
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => {
                setPlaying(false);
                onSelectStep(Math.max(0, safeIndex - 1));
              }}
              title="上一步"
            >
              <ChevronLeft size={14} />
              <span>上一步</span>
            </button>
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={() => setPlaying((p) => !p)}
            >
              {playing ? <Pause size={14} /> : <Play size={14} />}
              <span>{playing ? '暂停' : '播放'}</span>
            </button>
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => {
                setPlaying(false);
                onSelectStep(Math.min(steps.length - 1, safeIndex + 1));
              }}
              title="下一步"
            >
              <span>下一步</span>
              <ChevronRight size={14} />
            </button>
            <button type="button" className="btn btn-sm" onClick={() => jumpRound(1)} title="下一轮">
              <span>下一轮</span>
              <ChevronsRight size={14} />
            </button>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <div className="replay-pill-tabs">
              {([1, 2, 4] as const).map((spd) => (
                <button
                  key={spd}
                  type="button"
                  className={`replay-pill-tab${speed === spd ? ' active' : ''}`}
                  onClick={() => setSpeed(spd)}
                >
                  {spd}x
                </button>
              ))}
            </div>
            <span style={{ fontSize: 12.5, color: 'var(--text-secondary)', fontVariantNumeric: 'tabular-nums' }}>
              第 {current.round} 轮 · 步 {safeIndex + 1} / {steps.length}
            </span>
          </div>
        </div>

        <input
          type="range"
          min={0}
          max={steps.length - 1}
          value={safeIndex}
          onChange={(e) => {
            setPlaying(false);
            onSelectStep(Number(e.target.value));
          }}
          aria-label="回放步进滑块"
          style={{ width: '100%', accentColor: '#10b981' }}
        />

        <div className="replay-pill-tabs">
          {rounds.map((r) => (
            <button
              key={r}
              type="button"
              className={`replay-pill-tab${current.round === r ? ' active' : ''}`}
              onClick={() => {
                setPlaying(false);
                const idx = roundFirstIndex.get(r);
                if (idx != null) onSelectStep(idx);
              }}
            >
              R{r}
            </button>
          ))}
        </div>
      </section>

      <div className="replay-board-layout">
        <section className="panel replay-board-card">
          <ReplayBoardCanvas
            board={report.board}
            players={report.players}
            current={current}
            prevStep={safeIndex > 0 ? steps[safeIndex - 1] : undefined}
          />
        </section>

        <div style={{ display: 'grid', gap: 14, minWidth: 0 }}>
          <section className="panel" style={{ display: 'grid', gap: 8 }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
              <span className={`replay-badge ${currentMeta.tone}`}>{currentMeta.label}</span>
              <span style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                第 {current.round} 轮 · #{current.index + 1}
              </span>
            </div>
            <strong style={{ fontSize: 15 }}>{current.title}</strong>
            {current.detail && (
              <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', margin: 0, lineHeight: 1.5 }}>
                {current.detail}
              </p>
            )}
          </section>

          <section className="panel" style={{ display: 'grid', gap: 10 }}>
            <strong style={{ fontSize: 14 }}>当前四位角色实时状态与手牌</strong>
            {(current.players || []).map((sp, idx) => {
              const meta = playerMetaMap.get(sp.pid);
              const color = playerSeatColor(meta, idx + 1);
              const hpPct = sp.maxHp > 0 ? Math.max(0, Math.min(100, (sp.hp / sp.maxHp) * 100)) : 0;
              return (
                <div
                  key={`hud-${sp.pid}`}
                  style={{
                    border: `1px solid ${current.actorPid === sp.pid ? color : 'var(--border-color)'}`,
                    borderRadius: 10,
                    padding: '10px 12px',
                    background: 'var(--bg-input)',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: '6px 10px', justifyContent: 'space-between', flexWrap: 'wrap' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <div className="replay-hud-avatar">
                        {meta && (
                          <GameImage asset={heroImageKey(meta.heroId, `hud:${sp.pid}`)} name={heroDisplayName(meta)} className="portrait" />
                        )}
                      </div>
                      <strong style={{ fontSize: 13 }}>
                        <span style={{ color }}>{meta?.label || sp.pid}</span> {meta ? heroDisplayName(meta) : sp.pid}
                      </strong>
                    </div>
                    <div style={{ fontSize: 12, fontVariantNumeric: 'tabular-nums', color: 'var(--text-secondary)' }}>
                      格#{sp.node} · ★{sp.stars} · {sp.gold}币 ·{' '}
                      <Swords size={11} style={{ verticalAlign: 'middle' }} /> {sp.atk} /{' '}
                      <Shield size={11} style={{ verticalAlign: 'middle' }} /> {sp.def}
                    </div>
                  </div>

                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 6 }}>
                    <div style={{ flex: 1, height: 5, borderRadius: 99, background: 'var(--border-color)', overflow: 'hidden' }}>
                      <div style={{ width: `${hpPct}%`, height: '100%', background: sp.hp <= 0 ? '#94a3b8' : '#10b981' }} />
                    </div>
                    <span style={{ fontSize: 11, fontVariantNumeric: 'tabular-nums' }}>
                      HP {sp.hp}/{sp.maxHp}
                    </span>
                  </div>

                  {sp.cards.length > 0 && (
                    <div className="replay-card-chip-list">
                      {sp.cards.map((cid, ci) => {
                        const card = lookupCard(cid);
                        return (
                          <span
                            key={`${cid}-${ci}`}
                            className="replay-hand-pill"
                            title={card.description || card.name}
                          >
                            {card.name}
                          </span>
                        );
                      })}
                    </div>
                  )}

                  {sp.relics.length > 0 && (
                    <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 5 }}>
                      筹码：{sp.relics.map((rid) => lookupChip(rid).name).join(' · ')}
                    </div>
                  )}
                </div>
              );
            })}

            {current.monsters.length > 0 && (
              <div style={{ display: 'grid', gap: 6, marginTop: 4 }}>
                <strong style={{ fontSize: 13, display: 'flex', alignItems: 'center', gap: 6 }}>
                  <Skull size={14} color="#ef4444" />
                  <span>场上怪物 ({current.monsters.length})</span>
                </strong>
                {current.monsters.map((m, i) => (
                  <div
                    key={`m-${m.id}-${i}`}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      fontSize: 12,
                      padding: '6px 10px',
                      borderRadius: 8,
                      border: '1px solid var(--border-color)',
                      background: 'var(--bg-input)',
                    }}
                  >
                    <span>
                      {m.isBoss && <strong style={{ color: '#ef4444', marginRight: 5 }}>[BOSS]</strong>}
                      {m.name} · 格#{m.node}
                    </span>
                    <span style={{ fontVariantNumeric: 'tabular-nums' }}>
                      HP {m.hp}/{m.maxHp}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </section>

          <section className="panel" style={{ display: 'grid', gap: 10 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <strong style={{ fontSize: 14 }}>关键事件轴</strong>
              <select
                className="replay-select"
                value={kindFilter}
                onChange={(e) => setKindFilter(e.target.value)}
                style={{ padding: '4px 8px', fontSize: 12 }}
              >
                <option value="all">全部事件 ({steps.length})</option>
                <option value="battle">仅战斗</option>
                <option value="economy">商店 / 代付 / 升星</option>
                <option value="chip">筹码选择</option>
                <option value="mission">地图任务</option>
                <option value="card">出牌</option>
              </select>
            </div>
            <div className="replay-step-feed">
              {filteredSteps.map((st) => {
                const km = stepKindMeta(st.kind);
                return (
                  <button
                    key={st.index}
                    type="button"
                    className={`replay-step-row${st.index === safeIndex ? ' active' : ''}`}
                    onClick={() => {
                      setPlaying(false);
                      onSelectStep(st.index);
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                      <span className={`replay-badge ${km.tone}`}>{km.label}</span>
                      <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                        R{st.round} · #{st.index + 1}
                      </span>
                    </div>
                    <strong>{st.title}</strong>
                  </button>
                );
              })}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
};
