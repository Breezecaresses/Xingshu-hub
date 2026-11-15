import React, { useMemo, useState } from 'react';
import { Hash, MapPin } from 'lucide-react';
import type {
  ReplayAnalysisReport,
  ReplayBoardNode,
  ReplayStep,
  ReplayStepMonsterState,
  ReplayStepPlayerState,
} from '../../../shared/webCloudClient';
import { assetUrl } from '../../../shared/runtime';
import { playerColor, playerSeatColor } from './replayShared';

export interface ReplayBoardCanvasProps {
  board: ReplayAnalysisReport['board'];
  players: ReplayAnalysisReport['players'];
  current: ReplayStep;
  prevStep?: ReplayStep;
}

const LAND_ICON_BY_TYPE: Record<number, string> = {
  1: 'UT_Platform_108',
  2: 'UT_Platform_Start',
  3: 'UT_Platform_Teleport',
  4: 'UT_Platform_TeleportPoint',
  5: 'UT_Platform_Event',
  6: 'UT_Platform_Divine',
  7: 'UT_Platform_Shop',
  8: 'UT_Platform_Event',
  9: 'UT_Platform_Lottery',
  10: 'UT_Platform_Gamble',
  11: 'UT_Platform_Fire',
  12: 'UT_Platform_MoveAgain',
  13: 'UT_Platform_Hospital',
  14: 'UT_Platform_Card',
  15: 'UT_Platform_Gold',
  16: 'UT_Platform_Destiny',
  17: 'UT_Platform_Damage',
  18: 'UT_Platform_Jump',
  19: 'UT_Platform_Monster',
  20: 'UT_Platform_Heal',
  21: 'UT_Platform_TeleportPoint',
  22: 'UT_Platform_Shop',
  23: 'UT_Platform_Gift',
  24: 'UT_Platform_Relic',
  25: 'UT_Platform_Gimmick',
  26: 'UT_Platform_CandyGhost',
  27: 'UT_Platform_MindTraining',
  28: 'UT_Platform_Shop',
};

const SPAWN_STYLES: Record<number, { fill: string; text: string }> = {
  0: { fill: '#e53935', text: '#ffffff' },
  1: { fill: '#8bc34a', text: '#ffffff' },
  2: { fill: '#2563eb', text: '#ffffff' },
  3: { fill: '#f59e0b', text: '#ffffff' },
};

const LAND_FALLBACK_COLORS: Record<number, string> = {
  0: '#94a3b8',
  1: '#ef4444',
  2: '#94a3b8',
  3: '#38bdf8',
  4: '#3b82f6',
  6: '#7c3aed',
  7: '#10b981',
  8: '#ec4899',
  9: '#eab308',
  10: '#f59e0b',
  11: '#ef4444',
  12: '#2563eb',
  13: '#ef4444',
  14: '#22c55e',
  15: '#f59e0b',
  16: '#a855f7',
  17: '#dc2626',
  18: '#06b6d4',
  19: '#7c3aed',
  20: '#10b981',
  21: '#0ea5e9',
  22: '#10b981',
  23: '#f43f5e',
  24: '#d97706',
  25: '#db2777',
  26: '#d946ef',
  27: '#22c55e',
  28: '#10b981',
};

const GRID_CELL = 82;
const GRID_PAD = 56;

export const ReplayBoardCanvas: React.FC<ReplayBoardCanvasProps> = ({
  board,
  players,
  current,
  prevStep,
}) => {
  const [showIds, setShowIds] = useState(false);
  const [selectedNodeId, setSelectedNodeId] = useState<number | null>(null);
  const [failedIcons, setFailedIcons] = useState<Record<string, boolean>>({});

  const playerMetaMap = useMemo(() => {
    const map = new Map<string, (typeof players)[number]>();
    for (const p of players) map.set(p.id, p);
    return map;
  }, [players]);

  const nodes = board.nodes || [];
  const isOfficialGrid =
    board.layoutMode === 'official_grid' &&
    nodes.length > 0 &&
    nodes.every((n) => typeof n.gx === 'number' && typeof n.gy === 'number');

  const geometry = useMemo(() => {
    if (!isOfficialGrid) {
      return {
        vbW: 1000,
        vbH: 760,
        tileSize: 36,
        gridLinesX: [] as number[],
        gridLinesY: [] as number[],
        coords: new Map<number, { cx: number; cy: number }>(
          nodes.map((n) => [n.id, { cx: n.x, cy: n.y }]),
        ),
      };
    }
    const maxGx = Math.max(...nodes.map((n) => n.gx ?? 0), 1);
    const maxGy = Math.max(...nodes.map((n) => n.gy ?? 0), 1);
    const vbW = Math.max(380, Math.round(maxGx * GRID_CELL + 2 * GRID_PAD));
    const vbH = Math.max(340, Math.round(maxGy * GRID_CELL + 2 * GRID_PAD));
    const minSpacing = Math.max(0.68, board.minSpacing ?? 1);
    const tileSize = Math.min(GRID_CELL * 0.9, Math.max(GRID_CELL * 0.68, minSpacing * GRID_CELL * 0.88));
    const cols = Math.ceil(maxGx) + 2;
    const rows = Math.ceil(maxGy) + 2;
    const gridLinesX = Array.from({ length: cols }, (_, i) => GRID_PAD + i * GRID_CELL - GRID_CELL * 0.5);
    const gridLinesY = Array.from({ length: rows }, (_, i) => GRID_PAD + i * GRID_CELL - GRID_CELL * 0.5);
    const coords = new Map<number, { cx: number; cy: number }>(
      nodes.map((n) => [
        n.id,
        {
          cx: Math.round((GRID_PAD + (n.gx ?? 0) * GRID_CELL) * 10) / 10,
          cy: Math.round((GRID_PAD + (n.gy ?? 0) * GRID_CELL) * 10) / 10,
        },
      ]),
    );
    return { vbW, vbH, tileSize, gridLinesX, gridLinesY, coords };
  }, [isOfficialGrid, nodes, board.minSpacing]);

  const nodeMap = useMemo(() => {
    const map = new Map<number, ReplayBoardNode>();
    for (const n of nodes) map.set(n.id, n);
    return map;
  }, [nodes]);

  const playersByNode = useMemo(() => {
    const map = new Map<number, ReplayStepPlayerState[]>();
    for (const p of current.players || []) {
      const list = map.get(p.node) || [];
      list.push(p);
      map.set(p.node, list);
    }
    return map;
  }, [current.players]);

  const monstersByNode = useMemo(() => {
    const map = new Map<number, ReplayStepMonsterState[]>();
    for (const m of current.monsters || []) {
      const list = map.get(m.node) || [];
      list.push(m);
      map.set(m.node, list);
    }
    return map;
  }, [current.monsters]);

  const actorCur = useMemo(
    () => (current.players || []).find((p) => p.pid === current.actorPid),
    [current.players, current.actorPid],
  );
  const actorPrev = useMemo(
    () => (prevStep?.players || []).find((p) => p.pid === current.actorPid),
    [prevStep?.players, current.actorPid],
  );
  const movedArc = useMemo(() => {
    if (!actorCur || !actorPrev || actorCur.node === actorPrev.node) return null;
    const from = geometry.coords.get(actorPrev.node);
    const to = geometry.coords.get(actorCur.node);
    if (!from || !to) return null;
    const dx = to.cx - from.cx;
    const dy = to.cy - from.cy;
    const dist = Math.hypot(dx, dy);
    if (dist < 1) return null;
    const midX = (from.cx + to.cx) / 2 - (dy / dist) * Math.min(48, dist * 0.22);
    const midY = (from.cy + to.cy) / 2 + (dx / dist) * Math.min(48, dist * 0.22);
    return { fromNode: actorPrev.node, toNode: actorCur.node, from, to, midX, midY };
  }, [actorCur, actorPrev, geometry.coords]);

  const activeNodeId =
    (selectedNodeId !== null && nodeMap.has(selectedNodeId) ? selectedNodeId : null) ??
    actorCur?.node ??
    nodes[0]?.id ??
    null;

  const activeNode = activeNodeId !== null ? nodeMap.get(activeNodeId) : undefined;
  const activeOccupants = activeNode ? playersByNode.get(activeNode.id) || [] : [];
  const activeMonsters = activeNode ? monstersByNode.get(activeNode.id) || [] : [];

  const halfTile = geometry.tileSize / 2;
  const iconRadius = isOfficialGrid ? geometry.tileSize * 0.39 : 16;

  return (
    <div className="replay-board-panel">
      <div className="replay-board-header">
        <div className="replay-board-title-group">
          <strong>
            {isOfficialGrid ? '标准棋盘拓扑' : '棋盘实时拓扑'} ({nodes.length} 格)
          </strong>
          {isOfficialGrid ? (
            <span className="replay-badge ok">官方地图布局</span>
          ) : (
            <span className="replay-badge">自动拓扑</span>
          )}
        </div>
        <div className="replay-board-tools">
          <span className="replay-board-legend" title="角色棋子配色（点击任意格子可查看详情）">
            {[1, 2, 3, 4].map((slotNum) => (
              <span key={`leg-${slotNum}`}>
                <i className="dot" style={{ background: playerColor(slotNum) }} /> P{slotNum}
              </span>
            ))}
          </span>
          <button
            type="button"
            className={`btn btn-sm replay-board-toggle ${showIds ? 'active' : ''}`}
            onClick={() => setShowIds((v) => !v)}
            title="切换显示每个格子的编号"
          >
            <Hash size={13} />
            <span>{showIds ? '隐藏格号' : '显示格号'}</span>
          </button>
        </div>
      </div>

      <div className="replay-svg-frame">
        <svg
          viewBox={`0 0 ${geometry.vbW} ${geometry.vbH}`}
          className="replay-board-svg"
          role="img"
          aria-label="对战棋盘地图"
        >
          <defs>
            <clipPath id="replay-tile-circle" clipPathUnits="objectBoundingBox">
              <circle cx="0.5" cy="0.5" r="0.49" />
            </clipPath>
            <marker
              id="replay-move-arrow"
              markerWidth="8"
              markerHeight="8"
              refX="6"
              refY="4"
              orient="auto"
            >
              <path d="M0,1 L7,4 L0,7 z" fill="#f97316" />
            </marker>
          </defs>

          {isOfficialGrid && (
            <g className="replay-grid-backdrop" aria-hidden="true">
              {geometry.gridLinesX.map((gx, i) => (
                <line
                  key={`gx-${i}`}
                  x1={gx}
                  y1={0}
                  x2={gx}
                  y2={geometry.vbH}
                  stroke="#e7ebf2"
                  strokeWidth={1}
                />
              ))}
              {geometry.gridLinesY.map((gy, i) => (
                <line
                  key={`gy-${i}`}
                  x1={0}
                  y1={gy}
                  x2={geometry.vbW}
                  y2={gy}
                  stroke="#e7ebf2"
                  strokeWidth={1}
                />
              ))}
            </g>
          )}

          {/* Connected track joints */}
          <g className="replay-board-edges">
            {(board.edges || []).map(([u, v]) => {
              const p1 = geometry.coords.get(u);
              const p2 = geometry.coords.get(v);
              if (!p1 || !p2) return null;
              if (isOfficialGrid) {
                return (
                  <g key={`e-${u}-${v}`}>
                    <line
                      x1={p1.cx}
                      y1={p1.cy}
                      x2={p2.cx}
                      y2={p2.cy}
                      stroke="#4b5160"
                      strokeWidth={19}
                      strokeLinecap="round"
                    />
                    <line
                      x1={p1.cx}
                      y1={p1.cy}
                      x2={p2.cx}
                      y2={p2.cy}
                      stroke="#6d7382"
                      strokeWidth={14}
                      strokeLinecap="round"
                    />
                  </g>
                );
              }
              return (
                <line
                  key={`e-${u}-${v}`}
                  x1={p1.cx}
                  y1={p1.cy}
                  x2={p2.cx}
                  y2={p2.cy}
                  stroke="#cbd5e1"
                  strokeWidth={3}
                  strokeLinecap="round"
                />
              );
            })}
          </g>

          {/* Tiles */}
          <g className="replay-board-tiles">
            {nodes.map((n) => {
              const pos = geometry.coords.get(n.id);
              if (!pos) return null;
              const { cx, cy } = pos;
              const iconKey = n.icon || LAND_ICON_BY_TYPE[n.type] || '';
              const isSelected = activeNodeId === n.id;
              const isActorTile = actorCur?.node === n.id;
              const spawnIdx = typeof n.spawn === 'number' ? n.spawn : 0;
              const spawnStyle = SPAWN_STYLES[spawnIdx] || SPAWN_STYLES[0];
              const fallbackFill = LAND_FALLBACK_COLORS[n.type] || '#94a3b8';

              if (!isOfficialGrid) {
                return (
                  <g
                    key={`n-${n.id}`}
                    className="replay-tile-hit"
                    onClick={() => setSelectedNodeId(n.id)}
                  >
                    <circle
                      cx={cx}
                      cy={cy}
                      r={n.isShop || n.isHome ? 20 : 15}
                      fill={n.isShop ? '#fef3c7' : n.isHome ? '#dcfce7' : '#ffffff'}
                      stroke={isSelected ? '#6366f1' : n.isShop ? '#f59e0b' : n.isHome ? '#10b981' : '#94a3b8'}
                      strokeWidth={isSelected ? 3 : n.isShop || n.isHome ? 2.4 : 1.5}
                    />
                    <text
                      x={cx}
                      y={cy + 3.5}
                      textAnchor="middle"
                      fontSize="10"
                      fontWeight={700}
                      fill="#1e293b"
                    >
                      {n.isShop ? `商${n.id}` : n.id}
                    </text>
                  </g>
                );
              }

              return (
                <g
                  key={`n-${n.id}`}
                  className="replay-tile-hit"
                  onClick={() => setSelectedNodeId(n.id)}
                >
                  <title>{`#${n.id} ${n.label}${typeof n.spawn === 'number' ? ` (P${n.spawn + 1})` : ''}`}</title>
                  {/* Tile square body */}
                  <rect
                    x={cx - halfTile}
                    y={cy - halfTile}
                    width={geometry.tileSize}
                    height={geometry.tileSize}
                    rx={9}
                    fill={isActorTile ? '#4d5568' : '#656a78'}
                    stroke={isSelected ? '#f97316' : isActorTile ? '#6366f1' : '#ffffff'}
                    strokeWidth={isSelected ? 3.5 : isActorTile ? 3 : 2.2}
                  />
                  {/* Subtle top bevel */}
                  <rect
                    x={cx - halfTile + 2}
                    y={cy - halfTile + 2}
                    width={geometry.tileSize - 4}
                    height={geometry.tileSize - 4}
                    rx={7}
                    fill="none"
                    stroke="rgba(255,255,255,0.16)"
                    strokeWidth={1}
                  />

                  {/* Center circular tile emblem */}
                  {n.type === 1 ? (
                    <g>
                      <circle
                        cx={cx}
                        cy={cy}
                        r={iconRadius}
                        fill={spawnStyle.fill}
                        stroke="#ffffff"
                        strokeWidth={2.2}
                      />
                      <text
                        x={cx}
                        y={cy + 7}
                        textAnchor="middle"
                        fontSize="20"
                        fontWeight={900}
                        letterSpacing="-0.5"
                        fill={spawnStyle.text}
                        stroke="#111827"
                        strokeWidth={0.7}
                        paintOrder="stroke"
                      >
                        P{spawnIdx + 1}
                      </text>
                    </g>
                  ) : iconKey && !failedIcons[iconKey] ? (
                    <g>
                      <circle cx={cx} cy={cy} r={iconRadius} fill={fallbackFill} />
                      <image
                        href={assetUrl(`platform-tiles/${iconKey}.webp`)}
                        x={cx - iconRadius}
                        y={cy - iconRadius}
                        width={iconRadius * 2}
                        height={iconRadius * 2}
                        clipPath="url(#replay-tile-circle)"
                        preserveAspectRatio="xMidYMid slice"
                        onError={() => setFailedIcons((prev) => (prev[iconKey] ? prev : { ...prev, [iconKey]: true }))}
                      />
                      <circle
                        cx={cx}
                        cy={cy}
                        r={iconRadius}
                        fill="none"
                        stroke="rgba(255,255,255,0.85)"
                        strokeWidth={1.6}
                      />
                    </g>
                  ) : (
                    <g>
                      <circle
                        cx={cx}
                        cy={cy}
                        r={iconRadius * 0.85}
                        fill={fallbackFill}
                        stroke="#ffffff"
                        strokeWidth={1.8}
                      />
                      <text
                        x={cx}
                        y={cy + 5}
                        textAnchor="middle"
                        fontSize="14"
                        fontWeight={800}
                        fill="#ffffff"
                      >
                        {n.id}
                      </text>
                    </g>
                  )}

                  {/* Optional corner node ID badge */}
                  {(showIds || isSelected) && (
                    <g pointerEvents="none">
                      <rect
                        x={cx - halfTile + 2}
                        y={cy - halfTile + 2}
                        width={n.id >= 100 ? 28 : 22}
                        height={15}
                        rx={4}
                        fill="rgba(15, 23, 42, 0.86)"
                      />
                      <text
                        x={cx - halfTile + 2 + (n.id >= 100 ? 14 : 11)}
                        y={cy - halfTile + 12.5}
                        textAnchor="middle"
                        fontSize="10"
                        fontWeight={700}
                        fill="#f8fafc"
                      >
                        {n.id}
                      </text>
                    </g>
                  )}
                </g>
              );
            })}
          </g>

          {/* Movement trail for current actor */}
          {movedArc && (
            <g pointerEvents="none">
              <path
                d={`M ${movedArc.from.cx} ${movedArc.from.cy} Q ${movedArc.midX} ${movedArc.midY} ${movedArc.to.cx} ${movedArc.to.cy}`}
                fill="none"
                stroke="#f97316"
                strokeWidth={3.5}
                strokeDasharray="7 5"
                strokeLinecap="round"
                markerEnd="url(#replay-move-arrow)"
              />
            </g>
          )}

          {/* Monster / Boss tokens */}
          <g className="replay-board-monsters" pointerEvents="none">
            {Array.from(monstersByNode.entries()).map(([nid, mList]) => {
              const pos = geometry.coords.get(nid);
              if (!pos) return null;
              return mList.map((m, idx) => {
                const spread = mList.length > 1 ? (idx - (mList.length - 1) / 2) * 24 : 0;
                const mx = pos.cx + spread;
                const my = pos.cy - (isOfficialGrid ? halfTile * 0.68 : 24);
                const badgeW = m.isBoss ? 48 : 38;
                return (
                  <g key={`mon-${nid}-${m.id}-${idx}`}>
                    <rect
                      x={mx - badgeW / 2}
                      y={my - 11}
                      width={badgeW}
                      height={20}
                      rx={10}
                      fill={m.isBoss ? '#991b1b' : '#b45309'}
                      stroke="#ffffff"
                      strokeWidth={1.8}
                    />
                    <text
                      x={mx}
                      y={my + 3}
                      textAnchor="middle"
                      fontSize="10.5"
                      fontWeight={800}
                      fill="#ffffff"
                    >
                      {m.isBoss ? `B·${m.hp}` : `M·${m.hp}`}
                    </text>
                  </g>
                );
              });
            })}
          </g>

          {/* Player tokens */}
          <g className="replay-board-players" pointerEvents="none">
            {Array.from(playersByNode.entries()).map(([nid, list]) => {
              const pos = geometry.coords.get(nid);
              if (!pos) return null;
              const offsetDist = isOfficialGrid ? halfTile * 0.56 : 16;
              return list.map((p, idx) => {
                const meta = playerMetaMap.get(p.pid);
                const rosterIdx = players.findIndex((item) => item.id === p.pid);
                const fallbackSlot = rosterIdx >= 0 ? rosterIdx + 1 : idx + 1;
                const col = playerSeatColor(meta, fallbackSlot);
                const angle =
                  list.length === 1
                    ? Math.PI / 4
                    : -Math.PI / 4 + (idx * (2 * Math.PI)) / list.length;
                const px = pos.cx + Math.cos(angle) * offsetDist;
                const py = pos.cy + Math.sin(angle) * offsetDist;
                const isActor = p.pid === current.actorPid;
                const dead = p.hp <= 0;
                return (
                  <g key={`pl-${p.pid}`} opacity={dead ? 0.55 : 1}>
                    {isActor && (
                      <circle
                        cx={px}
                        cy={py}
                        r={19}
                        fill="none"
                        stroke={col}
                        strokeWidth={3}
                        strokeOpacity={0.75}
                      />
                    )}
                    <circle
                      cx={px}
                      cy={py}
                      r={isActor ? 15 : 13}
                      fill={col}
                      stroke="#ffffff"
                      strokeWidth={2.4}
                    />
                    <text
                      x={px}
                      y={py + 4}
                      textAnchor="middle"
                      fontSize="10.5"
                      fontWeight={900}
                      fill="#ffffff"
                    >
                      {dead ? '休' : meta?.label || `P${fallbackSlot}`}
                    </text>
                  </g>
                );
              });
            })}
          </g>
        </svg>
      </div>

      {/* Tap / Hover Node Inspector */}
      {activeNode && (
        <div className="replay-board-inspector">
          <div className="replay-board-inspector-main">
            <MapPin size={14} />
            <strong>
              #{activeNode.id} · {activeNode.label}
            </strong>
            {typeof activeNode.spawn === 'number' && (
              <span className="replay-badge info">P{activeNode.spawn + 1} 出生点</span>
            )}
            {activeNode.isShop && <span className="replay-badge gold">商店格</span>}
            {activeNode.jumpTo ? (
              <span className="replay-badge accent">跳跃目标 #{activeNode.jumpTo}</span>
            ) : null}
          </div>
          <div className="replay-board-inspector-meta">
            {activeNode.neighbors && activeNode.neighbors.length > 0 && (
              <span>相邻格：{activeNode.neighbors.join('、')}</span>
            )}
            {activeOccupants.length > 0 && (
              <span>
                角色：
                {activeOccupants
                  .map((p) => {
                    const meta = playerMetaMap.get(p.pid);
                    return `${meta?.label || p.pid} ${meta?.heroName || ''} (HP ${p.hp}/${p.maxHp})`;
                  })
                  .join(' / ')}
              </span>
            )}
            {activeMonsters.length > 0 && (
              <span>
                怪物：{activeMonsters.map((m) => `${m.name} (HP ${m.hp}/${m.maxHp})`).join(' / ')}
              </span>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
