import React from 'react';
import {
  Layers,
  ShieldCheck,
  AlertTriangle,
  Zap,
  Play,
  User,
} from 'lucide-react';
import type { ResolvedRuntimeSession } from '../../../types/runtimeSession';

export interface ClientSessionStatusCardProps {
  /** All resolved active runtime client sessions */
  sessions: ResolvedRuntimeSession[];
  /** Active spectator execution connection ID */
  selectedSpectatorConnectionId?: string | null;
  /** Prompt message for missing spectator runtime */
  spectatorPrompt?: string | null;
  /** Active account mode ('USER_OWNED' | 'XINGSHU_MANAGED' | null) */
  accountMode?: string | null;
  /** Callback to launch TapTap or secondary game client */
  onLaunchSpectatorClient?: () => void;
  /** Localization flag */
  isZh?: boolean;
}

export const ClientSessionStatusCard: React.FC<ClientSessionStatusCardProps> = ({
  sessions,
  selectedSpectatorConnectionId,
  spectatorPrompt,
  accountMode = 'USER_OWNED',
  onLaunchSpectatorClient,
  isZh = true,
}) => {
  const isUserSpectatorMode =
    accountMode === 'USER_OWNED' || accountMode === 'USER_SPECTATOR';

  const mainSession = sessions.find((s) => s.role === 'MAIN' || s.role === 'PENDING_MAIN');
  const spectatorSession = sessions.find(
    (s) =>
      (s.role === 'USER_SPECTATOR' || s.role === 'PENDING_SPECTATOR') &&
      s.platformId === 'TAPTAP'
  );

  const isTapTapConnected = !!spectatorSession;

  // Header display metrics
  const spectatorExecutionText = spectatorSession
    ? `TapTap · ${spectatorSession.playerId ?? (isZh ? '等待识别' : 'Pending')}`
    : (isZh ? 'TapTap · 未连接 (等待启动)' : 'TapTap · Disconnected');

  const mainAccountText = mainSession
    ? `Steam CN · ${mainSession.playerId ?? (isZh ? '已连接' : 'Connected')}`
    : (isZh ? 'Steam CN · 未运行' : 'Steam CN · Offline');

  return (
    <div
      className="panel"
      style={{
        marginBottom: 20,
        padding: '16px 20px',
        backgroundColor: 'rgba(17, 24, 39, 0.85)',
        borderRadius: 12,
        border: '1px solid rgba(255, 255, 255, 0.1)',
        backdropFilter: 'blur(10px)',
        boxShadow: '0 4px 16px rgba(0, 0, 0, 0.35)',
      }}
    >
      {/* 1. Header Bar: Multi-Client Routing Title & Global Banner */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 12,
          paddingBottom: 14,
          marginBottom: 14,
          borderBottom: '1px solid rgba(255, 255, 255, 0.08)',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div
            style={{
              width: 34,
              height: 34,
              borderRadius: 8,
              backgroundColor: 'rgba(99, 102, 241, 0.18)',
              color: '#818cf8',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <Layers size={18} />
          </div>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <h3 style={{ fontSize: 16, fontWeight: 700, margin: 0, color: '#fff' }}>
                {isZh ? '多客户端运行状态与执行端路由' : 'Multi-Client Runtime Routing'}
              </h3>
              <span
                style={{
                  fontSize: 10,
                  fontWeight: 700,
                  color: '#a5b4fc',
                  backgroundColor: 'rgba(99, 102, 241, 0.2)',
                  padding: '1px 6px',
                  borderRadius: 4,
                  border: '1px solid rgba(99, 102, 241, 0.35)',
                }}
              >
                HS4-P1.5 DUAL-ROUTING
              </span>
            </div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)', marginTop: 2 }}>
              {isZh
                ? '主游戏账号与观战执行端独立并行运行，精准分流'
                : 'Primary game and dedicated spectator run in parallel with deterministic routing'}
            </div>
          </div>
        </div>

        {/* Global Dual-Role Header Summary */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          {/* Main Account Tag */}
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 6,
              padding: '4px 10px',
              borderRadius: 6,
              backgroundColor: mainSession ? 'rgba(34, 197, 94, 0.12)' : 'rgba(100, 116, 139, 0.12)',
              border: mainSession ? '1px solid rgba(34, 197, 94, 0.3)' : '1px solid rgba(100, 116, 139, 0.2)',
              fontSize: 12,
            }}
          >
            <span style={{ color: '#94a3b8' }}>{isZh ? '主游戏账号:' : 'Main:'}</span>
            <strong style={{ color: mainSession ? '#4ade80' : '#94a3b8', fontFamily: 'monospace' }}>
              {mainAccountText}
            </strong>
          </div>

          {/* Spectator Execution Runtime Tag */}
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 6,
              padding: '4px 10px',
              borderRadius: 6,
              backgroundColor: isTapTapConnected ? 'rgba(99, 102, 241, 0.2)' : 'rgba(245, 158, 11, 0.15)',
              border: isTapTapConnected ? '1px solid rgba(99, 102, 241, 0.45)' : '1px solid rgba(245, 158, 11, 0.35)',
              fontSize: 12,
            }}
          >
            <Zap size={13} color={isTapTapConnected ? '#818cf8' : '#fbbf24'} />
            <span style={{ color: isTapTapConnected ? '#c7d2fe' : '#fef3c7' }}>
              {isZh ? '观战执行端:' : 'Spectator Runtime:'}
            </span>
            <strong style={{ color: isTapTapConnected ? '#a5b4fc' : '#fbbf24', fontFamily: 'monospace' }}>
              {spectatorExecutionText}
            </strong>
          </div>
        </div>
      </div>

      {/* 2. Client Grid */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))',
          gap: 12,
          marginBottom: spectatorPrompt && !isTapTapConnected ? 12 : 0,
        }}
      >
        {sessions.map((client, idx) => {
          const isMain = client.role === 'MAIN' || client.role === 'PENDING_MAIN';
          const isSpectator = client.role === 'USER_SPECTATOR' || client.role === 'PENDING_SPECTATOR';
          const isSelectedSpectator =
            client.isSpectatorExecutionRuntime ||
            client.connectionId === selectedSpectatorConnectionId;

          const cardBorder = isSelectedSpectator
            ? '2px solid rgba(99, 102, 241, 0.8)'
            : isMain
            ? '1px solid rgba(34, 197, 94, 0.35)'
            : '1px solid rgba(255, 255, 255, 0.1)';

          const cardBg = isSelectedSpectator
            ? 'rgba(99, 102, 241, 0.08)'
            : isMain
            ? 'rgba(34, 197, 94, 0.05)'
            : 'rgba(255, 255, 255, 0.02)';

          return (
            <div
              key={client.connectionId || idx}
              style={{
                padding: '12px 14px',
                borderRadius: 8,
                backgroundColor: cardBg,
                border: cardBorder,
                display: 'flex',
                flexDirection: 'column',
                gap: 8,
                position: 'relative',
              }}
            >
              {/* Top row: Client Index, Platform, Status, Spectator Tag */}
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 700,
                      color: '#94a3b8',
                      backgroundColor: 'rgba(255, 255, 255, 0.06)',
                      padding: '1px 6px',
                      borderRadius: 4,
                    }}
                  >
                    {isZh ? `客户端 ${idx + 1}` : `Client ${idx + 1}`}
                  </span>

                  <span
                    style={{
                      fontSize: 12,
                      fontWeight: 700,
                      color: client.platformId === 'TAPTAP' ? '#38bdf8' : '#4ade80',
                      display: 'flex',
                      alignItems: 'center',
                      gap: 4,
                    }}
                  >
                    <ShieldCheck size={14} />
                    {client.platformId === 'TAPTAP'
                      ? 'TapTap'
                      : client.platformId === 'STEAM_CN'
                      ? 'Steam CN'
                      : client.platformId}
                  </span>

                  <span style={{ fontSize: 11, color: '#64748b', fontFamily: 'monospace' }}>
                    PID: {client.processId}
                  </span>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  {isSelectedSpectator && (
                    <span
                      style={{
                        fontSize: 11,
                        fontWeight: 700,
                        color: '#fff',
                        backgroundColor: '#6366f1',
                        padding: '2px 8px',
                        borderRadius: 4,
                        display: 'flex',
                        alignItems: 'center',
                        gap: 4,
                        boxShadow: '0 0 10px rgba(99, 102, 241, 0.4)',
                      }}
                    >
                      <Zap size={11} />
                      {isZh ? '当前观战执行端' : 'Active Spectator'}
                    </span>
                  )}

                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 600,
                      color: '#22c55e',
                      display: 'flex',
                      alignItems: 'center',
                      gap: 4,
                    }}
                  >
                    <span
                      style={{
                        width: 7,
                        height: 7,
                        borderRadius: '50%',
                        backgroundColor: '#22c55e',
                        display: 'inline-block',
                      }}
                    />
                    {isZh ? '已连接' : 'Connected'}
                  </span>
                </div>
              </div>

              {/* Bottom row: PlayerId & Role */}
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  fontSize: 12,
                  paddingTop: 6,
                  borderTop: '1px solid rgba(255, 255, 255, 0.05)',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <User size={13} color="#94a3b8" />
                  <span style={{ color: '#94a3b8' }}>PlayerId:</span>
                  <strong style={{ color: '#fff', fontFamily: 'monospace' }}>
                    {client.playerId !== null
                      ? client.playerId
                      : isZh
                      ? '识别中...'
                      : 'Pending...'}
                  </strong>
                </div>

                <div>
                  <span style={{ color: '#94a3b8', marginRight: 6 }}>{isZh ? '角色：' : 'Role:'}</span>
                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 700,
                      padding: '2px 8px',
                      borderRadius: 4,
                      backgroundColor: isMain
                        ? 'rgba(34, 197, 94, 0.15)'
                        : isSpectator
                        ? 'rgba(99, 102, 241, 0.18)'
                        : 'rgba(234, 179, 8, 0.15)',
                      color: isMain
                        ? '#4ade80'
                        : isSpectator
                        ? '#a5b4fc'
                        : '#fde047',
                      border: isMain
                        ? '1px solid rgba(34, 197, 94, 0.35)'
                        : isSpectator
                        ? '1px solid rgba(99, 102, 241, 0.4)'
                        : '1px solid rgba(234, 179, 8, 0.35)',
                    }}
                  >
                    {isZh ? client.roleLabelZh : client.roleLabelEn}
                  </span>
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* 3. Dedicated Offline Spectator Prompt (when TapTap is not yet connected) */}
      {isUserSpectatorMode && !isTapTapConnected && (
        <div
          style={{
            padding: '12px 16px',
            borderRadius: 8,
            backgroundColor: 'rgba(245, 158, 11, 0.08)',
            border: '1px solid rgba(245, 158, 11, 0.35)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: 12,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10, flex: 1, minWidth: 260 }}>
            <AlertTriangle size={18} color="#f59e0b" style={{ flexShrink: 0, marginTop: 2 }} />
            <div>
              <div style={{ fontSize: 13, fontWeight: 700, color: '#fbbf24' }}>
                {spectatorPrompt || (isZh
                  ? '专用观战账号尚未启动。请启动 TapTap 客户端并登录观战账号'
                  : 'Dedicated spectator account not running. Please launch TapTap with spectator account')}
              </div>
              <div style={{ fontSize: 12, color: '#cbd5e1', marginTop: 3, lineHeight: 1.4 }}>
                {isZh
                  ? '💡 提示：你的主游戏账号 (Steam CN) 正在独立运行，无需退出主账号！请直接启动 TapTap 客户端进入观战房间，星枢将自动路由并捕获 4 人全景手牌。'
                  : 'Note: Your primary account (Steam CN) can keep running! Launch TapTap to spectate side-by-side.'}
              </div>
            </div>
          </div>

          {onLaunchSpectatorClient && (
            <button
              className="btn btn-secondary"
              onClick={onLaunchSpectatorClient}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 6,
                padding: '6px 14px',
                fontSize: 12,
                color: '#38bdf8',
                borderColor: 'rgba(56, 189, 248, 0.4)',
              }}
            >
              <Play size={13} />
              <span>{isZh ? '启动 TapTap 观战端' : 'Launch TapTap'}</span>
            </button>
          )}
        </div>
      )}
    </div>
  );
};
