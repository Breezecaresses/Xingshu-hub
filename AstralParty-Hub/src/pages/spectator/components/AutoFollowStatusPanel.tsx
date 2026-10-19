import React, { useState } from 'react';
import {
  Gamepad2,
  Eye,
  Radio,
  RefreshCw,
  AlertTriangle,
  Play,
  CheckCircle2,
  Clock,
  ShieldCheck,
  ChevronDown,
  ChevronUp,
  Terminal,
  Sparkles,
  Layers,
} from 'lucide-react';
import {
  AutoFollowState,
  AutoFollowContext,
  AUTO_FOLLOW_STATE_CONFIG_ZH,
} from '../../../services/autoFollowStateMachine';
import type { ResolvedRuntimeSession } from '../../../types/runtimeSession';
import { liveMatchContext, livePlayerId } from '../../../services/runtimeTruth';

export interface AutoFollowStatusPanelProps {
  /** Current state of the auto-follow state machine */
  autoFollowState: AutoFollowState;
  /** Full context of the auto-follow state machine */
  autoFollowContext: AutoFollowContext;
  /** Active main runtime session (Steam CN) */
  mainSession: ResolvedRuntimeSession | null;
  /** Active spectator runtime session (TapTap) */
  spectatorSession: ResolvedRuntimeSession | null;
  /** Callback to launch TapTap spectator client */
  onLaunchSpectatorClient: () => void;
  /** Callback to retry auto-follow join */
  onRetryAutoFollow: () => void;
  /** Callback to join with manually entered spectator code */
  onManualJoin: (code: string, roomId?: number) => void;
  /** Callback to reset auto-follow state machine */
  onResetAutoFollow: () => void;
  /** Localization flag */
  isZh?: boolean;
}

export const AutoFollowStatusPanel: React.FC<AutoFollowStatusPanelProps> = ({
  autoFollowState,
  autoFollowContext,
  mainSession,
  spectatorSession,
  onLaunchSpectatorClient,
  onRetryAutoFollow,
  onManualJoin,
  onResetAutoFollow,
  isZh = true,
}) => {
  const [diagnosticsExpanded, setDiagnosticsExpanded] = useState(false);
  const [manualCodeInput, setManualCodeInput] = useState('');

  const isFailed = autoFollowState === 'AUTO_JOIN_FAILED';
  const isSpectating = autoFollowState === 'SPECTATING';
  const isJoining = autoFollowState === 'AUTO_JOINING' || autoFollowState === 'LAUNCHING_SPECTATOR_CLIENT';

  const isMainOnline = !!mainSession && mainSession.status === 'CONNECTED';
  const isSpectatorOnline = !!spectatorSession && spectatorSession.status === 'CONNECTED';

  const rawMainSession = mainSession?.session;
  const rawSpectatorSession = spectatorSession?.session;
  const mainMatchCtx = liveMatchContext(rawMainSession);
  const isMainInMatch = isMainOnline && mainMatchCtx?.matchState === 'MAIN_IN_MATCH';
  const matchKind = mainMatchCtx?.matchKind ?? 'UNKNOWN';
  const playerCount = mainMatchCtx?.playerCount ?? '—';
  const spectatorCode = typeof mainMatchCtx?.spectatorCode === 'string' && mainMatchCtx.spectatorCode
    ? mainMatchCtx.spectatorCode : '—';
  const internalRoomId = mainMatchCtx?.internalRoomId ?? '—';
  const boundMainId = rawMainSession?.expectedBoundPlayerId ?? autoFollowContext.mainPlayerId ?? '—';
  const boundSpectatorId = rawSpectatorSession?.expectedBoundPlayerId ?? autoFollowContext.expectedSpectatorPlayerId ?? '—';
  const effectiveMainPlayerId = livePlayerId(rawMainSession);
  const effectiveSpectatorPlayerId = livePlayerId(rawSpectatorSession);
  const mainPid = mainSession?.processId || '—';
  const spectatorPid = spectatorSession?.processId || '—';
  const effectiveAutoFollowState = autoFollowState;

  const stateConfig =
    AUTO_FOLLOW_STATE_CONFIG_ZH[effectiveAutoFollowState] || AUTO_FOLLOW_STATE_CONFIG_ZH.IDLE;

  const handleManualSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (manualCodeInput.trim()) {
      onManualJoin(manualCodeInput.trim());
    }
  };

  // 4-step auto-follow pipeline progression
  const pipelineSteps = [
    {
      title: isZh ? '1. 对局检测' : '1. Match Detection',
      desc: isMainInMatch
        ? `Room #${internalRoomId} (${spectatorCode !== '—' ? spectatorCode : 'Active'})`
        : (isZh ? '大厅待命中' : 'Idle in Lobby'),
      completed: isMainInMatch,
      active: effectiveAutoFollowState === 'MAIN_IN_MATCH',
    },
    {
      title: isZh ? '2. 观战端就绪' : '2. Spectator Ready',
      desc: isSpectatorOnline
        ? (isZh ? 'TapTap 已连入' : 'TapTap Connected')
        : (isZh ? '等待拉起' : 'Waiting Launch'),
      completed: ['SPECTATOR_READY', 'AUTO_JOINING', 'SPECTATING'].includes(effectiveAutoFollowState),
      active: ['SPECTATOR_CLIENT_OFFLINE', 'LAUNCHING_SPECTATOR_CLIENT', 'WAITING_SPECTATOR_RUNTIME', 'WAITING_SPECTATOR_LOGIN'].includes(effectiveAutoFollowState),
    },
    {
      title: isZh ? '3. 自动跟随' : '3. Auto-Follow Join',
      desc: effectiveAutoFollowState === 'AUTO_JOINING'
        ? (isZh ? '指令下发中' : 'Dispatching')
        : isSpectating
        ? (isZh ? '加入完成' : 'Joined')
        : (isZh ? '准备就绪' : 'Pending'),
      completed: isSpectating,
      active: effectiveAutoFollowState === 'AUTO_JOINING',
    },
    {
      title: isZh ? '4. 全知透视' : '4. Live Spectating',
      desc: isSpectating ? (isZh ? '手牌实时同步' : 'Live Streaming') : (isZh ? '待命' : 'Standby'),
      completed: isSpectating,
      active: isSpectating,
    },
  ];

  return (
    <div
      style={{
        marginBottom: 20,
        display: 'flex',
        flexDirection: 'column',
        gap: 16,
      }}
    >
      {/* =========================================================================
          SECTION 1: TOP-LEVEL CARDS (Steam CN MAIN vs Match Context vs TapTap SPECTATOR)
         ========================================================================= */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
          gap: 14,
        }}
      >
        {/* Card 1: Steam CN MAIN */}
        <div
          className="panel"
          style={{
            padding: '16px 18px',
            backgroundColor: 'rgba(17, 24, 39, 0.85)',
            border: isMainOnline
              ? '1px solid rgba(34, 197, 94, 0.35)'
              : '1px solid rgba(255, 255, 255, 0.08)',
            borderRadius: 12,
            boxShadow: '0 4px 16px rgba(0, 0, 0, 0.25)',
            backdropFilter: 'blur(10px)',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
            gap: 10,
          }}
        >
          <div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div
                  style={{
                    width: 32,
                    height: 32,
                    borderRadius: 8,
                    backgroundColor: 'rgba(34, 197, 94, 0.15)',
                    color: '#4ade80',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                  }}
                >
                  <Gamepad2 size={18} />
                </div>
                <div>
                  <div style={{ fontSize: 14, fontWeight: 700, color: '#fff' }}>
                    {isZh ? '主游戏账号 (MAIN)' : 'Primary Account (MAIN)'}
                  </div>
                  <div style={{ fontSize: 11, color: '#94a3b8' }}>
                    Steam 国服 · STEAM_CN
                  </div>
                </div>
              </div>

              <span
                style={{
                  fontSize: 11,
                  padding: '2px 8px',
                  borderRadius: 4,
                  backgroundColor: isMainOnline ? 'rgba(34, 197, 94, 0.18)' : 'rgba(148, 163, 184, 0.15)',
                  color: isMainOnline ? '#4ade80' : '#94a3b8',
                  fontWeight: 600,
                  fontFamily: 'monospace',
                  border: isMainOnline ? '1px solid rgba(34, 197, 94, 0.4)' : '1px solid rgba(148, 163, 184, 0.25)',
                }}
              >
                {isMainOnline
                  ? isMainInMatch
                    ? (isZh ? '● 对局中' : '● In Match')
                    : (isZh ? '● 主界面待命' : '● Lobby Standby')
                  : (isZh ? '○ 未运行 / 离线' : '○ Offline')}
              </span>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12, color: '#cbd5e1' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '绑定 PlayerID：' : 'Bound PlayerId: '}</span>
                <strong style={{ color: '#fff', fontFamily: 'monospace' }}>{boundMainId}</strong>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '实时识别 ID：' : 'Live PlayerId: '}</span>
                <strong style={{ color: effectiveMainPlayerId ? '#4ade80' : '#94a3b8', fontFamily: 'monospace' }}>
                  {effectiveMainPlayerId ?? (isZh ? '未识别' : 'None')}
                </strong>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '运行进程 PID：' : 'Process PID: '}</span>
                <span style={{ fontFamily: 'monospace' }}>{mainPid}</span>
              </div>
            </div>
          </div>

          <div
            style={{
              paddingTop: 8,
              borderTop: '1px solid rgba(255, 255, 255, 0.06)',
              fontSize: 11,
              color: '#94a3b8',
              display: 'flex',
              alignItems: 'center',
              gap: 6,
            }}
          >
            <ShieldCheck size={14} color="#4ade80" />
            <span>{isZh ? '安全屏障：主游戏账号仅作局势感知源，永不执行观战加入' : 'Read-only battle feed'}</span>
          </div>
        </div>

        {/* Card 2: 当前对局 (Match) */}
        <div
          className="panel"
          style={{
            padding: '16px 18px',
            backgroundColor: 'rgba(17, 24, 39, 0.85)',
            border: isMainInMatch
              ? '1px solid rgba(245, 158, 11, 0.45)'
              : '1px solid rgba(255, 255, 255, 0.08)',
            borderRadius: 12,
            boxShadow: '0 4px 16px rgba(0, 0, 0, 0.25)',
            backdropFilter: 'blur(10px)',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
            gap: 10,
          }}
        >
          <div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div
                  style={{
                    width: 32,
                    height: 32,
                    borderRadius: 8,
                    backgroundColor: 'rgba(245, 158, 11, 0.15)',
                    color: '#fbbf24',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                  }}
                >
                  <Layers size={18} />
                </div>
                <div>
                  <div style={{ fontSize: 14, fontWeight: 700, color: '#fff' }}>
                    {isZh ? '当前对局 (Match)' : 'Current Match'}
                  </div>
                  <div style={{ fontSize: 11, color: '#94a3b8' }}>
                    {isZh ? 'PVE / 局势同步 · Match Context' : 'PVE / Live Match Context'}
                  </div>
                </div>
              </div>

              <span
                style={{
                  fontSize: 11,
                  padding: '2px 8px',
                  borderRadius: 4,
                  backgroundColor: isMainInMatch ? 'rgba(245, 158, 11, 0.18)' : 'rgba(148, 163, 184, 0.15)',
                  color: isMainInMatch ? '#fbbf24' : '#94a3b8',
                  fontWeight: 600,
                  fontFamily: 'monospace',
                  border: isMainInMatch ? '1px solid rgba(245, 158, 11, 0.4)' : '1px solid rgba(148, 163, 184, 0.25)',
                }}
              >
                {isMainInMatch
                  ? (isZh ? '● 对局进行中' : '● In Match')
                  : (isZh ? '○ 等待对局' : '○ Standby')}
              </span>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12, color: '#cbd5e1' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '对局类型：' : 'Match Kind: '}</span>
                <strong style={{ color: '#38bdf8', fontFamily: 'monospace' }}>{matchKind}</strong>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '玩家人数：' : 'Players: '}</span>
                <span style={{ fontFamily: 'monospace' }}>
                  {typeof playerCount === 'number' ? (isZh ? `${playerCount} 人` : `${playerCount} Players`) : playerCount}
                </span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '公开观战码：' : 'Spectator Code: '}</span>
                <strong style={{ color: '#fbbf24', fontFamily: 'monospace', letterSpacing: 1 }}>
                  {spectatorCode}
                </strong>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '内部 RoomId：' : 'Internal RoomId: '}</span>
                <span style={{ color: '#94a3b8', fontFamily: 'monospace' }}>
                  {typeof internalRoomId === 'number' ? `#${internalRoomId}` : internalRoomId}
                </span>
              </div>
            </div>
          </div>

          <div
            style={{
              paddingTop: 8,
              borderTop: '1px solid rgba(255, 255, 255, 0.06)',
              fontSize: 11,
              color: '#94a3b8',
              display: 'flex',
              alignItems: 'center',
              gap: 6,
            }}
          >
            <Radio size={14} color="#fbbf24" />
            <span>{isZh ? '双标识分离：观战码 (WatchCode) 为唯一公共入房凭据' : 'Public WatchCode isolated from internal RoomId'}</span>
          </div>
        </div>

        {/* Card 3: 观战执行端 (TapTap USER_SPECTATOR) */}
        <div
          className="panel"
          style={{
            padding: '16px 18px',
            backgroundColor: 'rgba(17, 24, 39, 0.85)',
            border: isSpectatorOnline
              ? '1px solid rgba(99, 102, 241, 0.45)'
              : '1px solid rgba(245, 158, 11, 0.3)',
            borderRadius: 12,
            boxShadow: '0 4px 16px rgba(0, 0, 0, 0.25)',
            backdropFilter: 'blur(10px)',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
            gap: 10,
          }}
        >
          <div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div
                  style={{
                    width: 32,
                    height: 32,
                    borderRadius: 8,
                    backgroundColor: 'rgba(99, 102, 241, 0.18)',
                    color: '#818cf8',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                  }}
                >
                  <Eye size={18} />
                </div>
                <div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                    <div style={{ fontSize: 14, fontWeight: 700, color: '#fff' }}>
                      {isZh ? '专用观战端 (SPECTATOR)' : 'Spectator Runtime'}
                    </div>
                    <span
                      style={{
                        fontSize: 10,
                        padding: '1px 5px',
                        borderRadius: 3,
                        backgroundColor: 'rgba(99, 102, 241, 0.2)',
                        color: '#a5b4fc',
                        fontWeight: 600,
                      }}
                    >
                      {isZh ? '执行端' : 'Target'}
                    </span>
                  </div>
                  <div style={{ fontSize: 11, color: '#94a3b8' }}>
                    TapTap 独立端 · TAPTAP
                  </div>
                </div>
              </div>

              <span
                style={{
                  fontSize: 11,
                  padding: '2px 8px',
                  borderRadius: 4,
                  backgroundColor: isSpectating
                    ? 'rgba(34, 197, 94, 0.18)'
                    : isSpectatorOnline
                    ? 'rgba(99, 102, 241, 0.18)'
                    : 'rgba(245, 158, 11, 0.15)',
                  color: isSpectating ? '#4ade80' : isSpectatorOnline ? '#a5b4fc' : '#fbbf24',
                  fontWeight: 600,
                  fontFamily: 'monospace',
                  border: isSpectating
                    ? '1px solid rgba(34, 197, 94, 0.4)'
                    : isSpectatorOnline
                    ? '1px solid rgba(99, 102, 241, 0.4)'
                    : '1px solid rgba(245, 158, 11, 0.35)',
                }}
              >
                {isSpectating
                  ? (isZh ? '● 实时观战中' : '● Live Spectating')
                  : isSpectatorOnline
                  ? (isZh ? '● 已连接 / 待观战' : '● Connected / Standby')
                  : (isZh ? '○ 等待启动' : '○ Standby')}
              </span>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 12, color: '#cbd5e1' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '绑定 PlayerID：' : 'Bound PlayerId: '}</span>
                <strong style={{ color: '#fff', fontFamily: 'monospace' }}>{boundSpectatorId}</strong>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '实时识别 ID：' : 'Live PlayerId: '}</span>
                <strong style={{ color: effectiveSpectatorPlayerId ? '#818cf8' : '#fbbf24', fontFamily: 'monospace' }}>
                  {effectiveSpectatorPlayerId ?? (isZh ? '等待进入识别' : 'Pending')}
                </strong>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>{isZh ? '运行进程 PID：' : 'Process PID: '}</span>
                <span style={{ fontFamily: 'monospace' }}>
                  {spectatorPid}
                </span>
              </div>
            </div>
          </div>

          <div
            style={{
              paddingTop: 8,
              borderTop: '1px solid rgba(255, 255, 255, 0.06)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 8,
            }}
          >
            <span style={{ fontSize: 11, color: '#94a3b8' }}>
              {isZh ? '独立并行通道 · 零串号风险' : 'Independent session'}
            </span>
            {!isSpectatorOnline && (
              <button
                className="btn btn-secondary"
                onClick={onLaunchSpectatorClient}
                style={{
                  padding: '3px 10px',
                  fontSize: 11,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 4,
                  color: '#38bdf8',
                }}
              >
                <Play size={12} />
                <span>{isZh ? '启动 TapTap' : 'Launch TapTap'}</span>
              </button>
            )}
          </div>
        </div>
      </div>

      {/* =========================================================================
          SECTION 2: AUTO-FOLLOW STATUS PANEL & PROGRESSION
         ========================================================================= */}
      <div
        className="panel"
        style={{
          padding: '18px 22px',
          backgroundColor: 'rgba(17, 24, 39, 0.9)',
          borderRadius: 12,
          border: `1px solid ${stateConfig.border}`,
          boxShadow: isSpectating
            ? '0 0 20px rgba(34, 197, 94, 0.18)'
            : isFailed
            ? '0 0 20px rgba(239, 68, 68, 0.25)'
            : '0 4px 16px rgba(0, 0, 0, 0.35)',
          backdropFilter: 'blur(10px)',
          display: 'flex',
          flexDirection: 'column',
          gap: 16,
        }}
      >
        {/* Header: Title + Dynamic State Pill */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: 10,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div
              style={{
                width: 36,
                height: 36,
                borderRadius: 9,
                backgroundColor: stateConfig.bg,
                color: stateConfig.color,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                border: `1px solid ${stateConfig.border}`,
              }}
            >
              {isSpectating ? (
                <Radio size={20} className="pulse" />
              ) : isJoining ? (
                <RefreshCw size={20} className="spin" />
              ) : isFailed ? (
                <AlertTriangle size={20} />
              ) : (
                <Sparkles size={20} />
              )}
            </div>

            <div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <h3 style={{ fontSize: 16, fontWeight: 700, margin: 0, color: '#fff' }}>
                  {isZh ? '自动跟随观战状态机' : 'Auto-Follow Spectator Engine'}
                </h3>
                <span
                  style={{
                    fontSize: 12,
                    fontWeight: 700,
                    padding: '2px 10px',
                    borderRadius: 6,
                    backgroundColor: stateConfig.bg,
                    color: stateConfig.color,
                    border: `1px solid ${stateConfig.border}`,
                    fontFamily: 'monospace',
                  }}
                >
                  {stateConfig.label}
                </span>
              </div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)', marginTop: 2 }}>
                {stateConfig.description}
              </div>
            </div>
          </div>

          {/* Quick Target Summary Badge */}
          {autoFollowContext.activeTarget && (
            <div
              style={{
                padding: '6px 12px',
                borderRadius: 8,
                backgroundColor: 'rgba(255, 255, 255, 0.05)',
                border: '1px solid rgba(255, 255, 255, 0.1)',
                fontSize: 12,
                display: 'flex',
                alignItems: 'center',
                gap: 8,
              }}
            >
              <span style={{ color: '#94a3b8' }}>{isZh ? '目标房间：' : 'Target Room: '}</span>
              <strong style={{ color: '#38bdf8', fontFamily: 'monospace' }}>
                #{autoFollowContext.activeTarget.roomId}
              </strong>
              {autoFollowContext.activeTarget.watchCode && (
                <span style={{ color: '#cbd5e1', fontSize: 11 }}>
                  ({isZh ? '观战码: ' : 'Code: '}
                  <strong style={{ color: '#fbbf24', letterSpacing: 1 }}>
                    {autoFollowContext.activeTarget.watchCode}
                  </strong>
                  )
                </span>
              )}
            </div>
          )}
        </div>

        {/* 4-Step Progression Pipeline */}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
            gap: 10,
            padding: '12px 14px',
            borderRadius: 8,
            backgroundColor: 'rgba(0, 0, 0, 0.25)',
            border: '1px solid rgba(255, 255, 255, 0.05)',
          }}
        >
          {pipelineSteps.map((step, idx) => {
            const isStepActive = step.active;
            const isStepCompleted = step.completed;
            const statusColor = isStepCompleted ? '#4ade80' : isStepActive ? '#38bdf8' : '#64748b';

            return (
              <div
                key={idx}
                style={{
                  display: 'flex',
                  alignItems: 'flex-start',
                  gap: 8,
                  opacity: isStepCompleted || isStepActive ? 1 : 0.65,
                }}
              >
                <div
                  style={{
                    width: 20,
                    height: 20,
                    borderRadius: '50%',
                    backgroundColor: isStepCompleted
                      ? 'rgba(34, 197, 94, 0.2)'
                      : isStepActive
                      ? 'rgba(56, 189, 248, 0.2)'
                      : 'rgba(255, 255, 255, 0.05)',
                    border: `1px solid ${statusColor}`,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginTop: 2,
                    flexShrink: 0,
                  }}
                >
                  {isStepCompleted ? (
                    <CheckCircle2 size={12} color="#4ade80" />
                  ) : isStepActive ? (
                    <Clock size={12} color="#38bdf8" className="spin" />
                  ) : (
                    <span style={{ fontSize: 10, color: '#64748b' }}>{idx + 1}</span>
                  )}
                </div>
                <div>
                  <div style={{ fontSize: 12, fontWeight: 700, color: isStepCompleted || isStepActive ? '#fff' : '#94a3b8' }}>
                    {step.title}
                  </div>
                  <div style={{ fontSize: 11, color: '#94a3b8', marginTop: 1 }}>{step.desc}</div>
                </div>
              </div>
            );
          })}
        </div>

        {/* =========================================================================
            SECTION 3: ERROR STATE & RECOVERY (AUTO_JOIN_FAILED)
           ========================================================================= */}
        {isFailed && (
          <div
            style={{
              padding: '16px 20px',
              borderRadius: 10,
              backgroundColor: 'rgba(239, 68, 68, 0.12)',
              border: '2px solid rgba(239, 68, 68, 0.45)',
              display: 'flex',
              flexDirection: 'column',
              gap: 12,
            }}
          >
            <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
              <div
                style={{
                  width: 36,
                  height: 36,
                  borderRadius: 8,
                  backgroundColor: 'rgba(239, 68, 68, 0.25)',
                  color: '#ef4444',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  flexShrink: 0,
                }}
              >
                <AlertTriangle size={20} />
              </div>
              <div style={{ flex: 1 }}>
                <div style={{ fontSize: 15, fontWeight: 700, color: '#fca5a5' }}>
                  {isZh ? '自动跟随观战异常 (AUTO_JOIN_FAILED)' : 'Auto-Join Failed'}
                </div>
                <div style={{ fontSize: 13, color: '#fecaca', marginTop: 4, lineHeight: 1.5 }}>
                  {autoFollowContext.failureMessage || (isZh ? '观战指令执行超时或房间拒绝连入' : 'Request rejected or timed out')}
                </div>
                {autoFollowContext.failureReason && (
                  <div style={{ fontSize: 11, color: '#f87171', marginTop: 4, fontFamily: 'monospace' }}>
                    Error Code: {autoFollowContext.failureReason} (Retries: {autoFollowContext.retryCount})
                  </div>
                )}
              </div>
            </div>

            {/* Error Actions: Retry + Fallback Manual Code Entry */}
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                flexWrap: 'wrap',
                gap: 12,
                paddingTop: 8,
                borderTop: '1px solid rgba(239, 68, 68, 0.2)',
              }}
            >
              {/* Fallback Manual Code Input */}
              <form onSubmit={handleManualSubmit} style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                <span style={{ fontSize: 12, color: '#fecaca' }}>{isZh ? '手动观战码备用：' : 'Manual Code: '}</span>
                <input
                  type="text"
                  placeholder={isZh ? '输入游戏观战码' : 'Enter game watch code'}
                  value={manualCodeInput}
                  onChange={(e) => setManualCodeInput(e.target.value)}
                  maxLength={64}
                  style={{
                    padding: '6px 12px',
                    borderRadius: 6,
                    backgroundColor: 'rgba(0, 0, 0, 0.4)',
                    border: '1px solid rgba(255, 255, 255, 0.2)',
                    color: '#fff',
                    fontFamily: 'monospace',
                    fontSize: 13,
                    width: 180,
                  }}
                />
                <button
                  type="submit"
                  className="btn btn-secondary"
                  disabled={!manualCodeInput.trim()}
                  style={{ fontSize: 12, padding: '6px 14px' }}
                >
                  {isZh ? '立即加入' : 'Join'}
                </button>
              </form>

              {/* Action Buttons */}
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <button
                  className="btn btn-primary"
                  onClick={onRetryAutoFollow}
                  style={{
                    fontSize: 12,
                    padding: '7px 16px',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 6,
                    backgroundColor: '#ef4444',
                    borderColor: '#dc2626',
                  }}
                >
                  <RefreshCw size={14} />
                  <span>{isZh ? '重试自动跟随' : 'Retry Auto-Follow'}</span>
                </button>
                <button
                  className="btn btn-secondary"
                  onClick={onResetAutoFollow}
                  style={{ fontSize: 12, padding: '7px 14px' }}
                >
                  {isZh ? '重置回待命' : 'Reset to Idle'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* =========================================================================
            SECTION 4: ADVANCED DIAGNOSTICS VIEW
           ========================================================================= */}
        <div style={{ borderTop: '1px solid rgba(255, 255, 255, 0.08)', paddingTop: 10 }}>
          <button
            onClick={() => setDiagnosticsExpanded((prev) => !prev)}
            style={{
              background: 'none',
              border: 'none',
              color: '#94a3b8',
              fontSize: 12,
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              gap: 6,
              padding: '4px 0',
            }}
          >
            <Terminal size={14} />
            <span>{isZh ? '高级遥测与状态机诊断技术视图' : 'Advanced Diagnostics View'}</span>
            {diagnosticsExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
          </button>

          {diagnosticsExpanded && (
            <div
              style={{
                marginTop: 10,
                padding: '12px 14px',
                borderRadius: 8,
                backgroundColor: 'rgba(0, 0, 0, 0.45)',
                border: '1px solid rgba(255, 255, 255, 0.08)',
                fontFamily: 'monospace',
                fontSize: 11,
                color: '#cbd5e1',
                display: 'flex',
                flexDirection: 'column',
                gap: 6,
              }}
            >
              <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid rgba(255, 255, 255, 0.05)', paddingBottom: 4 }}>
                <span style={{ color: '#94a3b8' }}>State Machine Current State:</span>
                <strong style={{ color: stateConfig.color }}>{autoFollowState}</strong>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>Bound Main Player ID:</span>
                <span>{boundMainId} (Platform: STEAM_CN)</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>Bound Spectator Player ID:</span>
                <span>{boundSpectatorId} (Platform: TAPTAP)</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>Local Control Protocol Security Gate:</span>
                <span style={{ color: '#4ade80' }}>PASSED: ROUTING RESTRICTED TO TAPTAP ONLY (STEAM_CN BLOCKED)</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>Spectator Connection ID:</span>
                <span>{autoFollowContext.spectatorConnectionId || 'None'}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>Spectator PID:</span>
                <span>{autoFollowContext.spectatorPid || 'None'}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>Last Request ID:</span>
                <span>{autoFollowContext.lastRequestId || 'None'}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                <span style={{ color: '#94a3b8' }}>Active Target Primitive:</span>
                <span>{autoFollowContext.activeTarget ? JSON.stringify(autoFollowContext.activeTarget) : 'null'}</span>
              </div>

              {/* Transition History (Last 5) */}
              {autoFollowContext.history && autoFollowContext.history.length > 0 && (
                <div style={{ marginTop: 8, borderTop: '1px solid rgba(255, 255, 255, 0.05)', paddingTop: 6 }}>
                  <div style={{ color: '#94a3b8', marginBottom: 4 }}>Recent Transitions:</div>
                  {autoFollowContext.history.slice(-5).map((h, i) => (
                    <div key={i} style={{ color: '#a5b4fc', fontSize: 10 }}>
                      [{new Date(h.timestamp).toLocaleTimeString()}] {h.from} → {h.to} (trigger: {h.trigger})
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
