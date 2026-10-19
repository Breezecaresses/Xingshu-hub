import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Eye,
  RefreshCw,
  Radio,
  Users,
  Layers,
  ShieldCheck,
  Clock,
  Sparkles,
  Gamepad2,
  UserCheck,
  AlertTriangle,
  ChevronDown,
  ChevronUp,
  RotateCcw,
  Play,
} from 'lucide-react';
import { useSpectatorState, SpectatorLifecycleState } from './useSpectatorState';
import { formatSpectatorPlayerId } from './spectatorTransition';
import { SpectatorStepIndicator } from './components/SpectatorStepIndicator';
import { SpectatorAccountSelector } from './components/SpectatorAccountSelector';
import { ClientSessionStatusCard } from './components/ClientSessionStatusCard';
import { AutoFollowStatusPanel } from './components/AutoFollowStatusPanel';
import {
  groupCardsForDisplay,
  getCardCategoryBadgeStyle,
  DisplayCardItem,
} from '../../shared/cardRegistry';
import { useTranslation } from '../../i18n';
import type { PlayerHandSummary } from '../../shared/types';

export const SpectatorPage: React.FC = () => {
  const { locale } = useTranslation();
  const isZh = locale === 'zh-CN';
  const navigate = useNavigate();

  const {
    lifecycleState,
    liveStreaming,
    isSnapshotStale,
    roomState,
    isConnected,
    isGameRunning,
    gamePid,
    spectatorPlayerId,
    accountMode,
    setAccountMode,
    userAccountInfo,
    managedAccountInfo,
    startLocalManagedValidation,
    launchGame,
    isRefreshing,
    manualRefresh,
    isRoleMismatch,
    roleMismatchWarning,

    resolvedSessions,
    selectedSpectatorConnectionId,
    mainSession,
    spectatorSession,
    spectatorPrompt,
    launchSpectatorClient,

    autoFollowState,
    autoFollowContext,
    retryAutoFollow,
    manualJoinAutoFollow,
    resetAutoFollow,
  } = useSpectatorState();

  const [bannerExpanded, setBannerExpanded] = useState(() => {
    if (typeof window !== 'undefined' && window.location.search.includes('view=hands')) {
      return false;
    }
    return true;
  });
  const [selectorForceVisible, setSelectorForceVisible] = useState(false);

  React.useEffect(() => {
    if (typeof window !== 'undefined' && window.location.search.includes('view=hands')) {
      const timer = setTimeout(() => {
        const el = document.getElementById('spectator-hand-grid');
        if (el) {
          el.scrollIntoView({ behavior: 'instant', block: 'start' });
        }
      }, 600);
      return () => clearTimeout(timer);
    }
  }, []);

  const platformNames: Record<string, string> = { TAPTAP: 'TapTap', BILIBILI: '哔哩哔哩', STEAM_CN: 'Steam 国服', STEAM_GLOBAL: 'Steam 国际服', WEGAME: 'WeGame' };
  const spectatorPlatformName = platformNames[autoFollowContext.targetPlatform] ?? '已绑定平台';
  const mainPlatformName = platformNames[mainSession?.platformId ?? ''] ?? '已绑定平台';
  const effectiveMainId = mainSession?.playerId ?? null;
  const effectiveSpectatorId = spectatorSession?.playerId ?? null;

  // Formats dual-client routing summary text per HS4-P1.5 specification
  const spectatorSummaryText = spectatorSession
    ? `观战执行端：${spectatorPlatformName} · ${effectiveSpectatorId ?? (isZh ? '等待识别' : 'Pending')}`
    : (isZh ? `观战执行端：${spectatorPlatformName} · 未连接` : `Spectator: ${spectatorPlatformName} · Disconnected`);

  const mainSummaryText = mainSession
    ? `主游戏账号：${mainPlatformName} · ${effectiveMainId ?? (isZh ? '已连接' : 'Connected')}`
    : (isZh ? '主游戏账号：Steam CN · 未连接' : 'Main: Steam CN · Disconnected');

  const hasRoleMismatch = isRoleMismatch;

  // Parse 4 remote players from roomState
  const remotePlayerEntries = Object.entries(roomState.roomPlayers || {});
  const hasActiveRoom = roomState.roomId !== null && remotePlayerEntries.length > 0;

  const getLifecycleBadge = (state: SpectatorLifecycleState) => {
    if (!isConnected && hasActiveRoom) {
      return {
        label: isZh ? '● 最后快照 (Runtime 已断开 · 数据已过期)' : '● Last Snapshot (Runtime Disconnected · Stale)',
        className: 'status-pill error',
        color: '#ef4444',
        bg: 'rgba(239, 68, 68, 0.15)',
      };
    }
    switch (state) {
      case 'STREAMING':
        if (!isConnected) {
          return {
            label: isZh ? '● 最后快照 (Runtime 已断开 · 数据已过期)' : '● Last Snapshot (Runtime Disconnected · Stale)',
            className: 'status-pill error',
            color: '#ef4444',
            bg: 'rgba(239, 68, 68, 0.15)',
          };
        }
        return {
          label: isZh ? '● 实时手牌传输中' : '● Live Streaming',
          className: 'status-pill ok',
          color: '#22c55e',
          bg: 'rgba(34, 197, 94, 0.15)',
        };
      case 'SYNCHRONIZING':
        return {
          label: isZh ? '○ 同步房间数据中...' : '○ Synchronizing...',
          className: 'status-pill warning',
          color: '#eab308',
          bg: 'rgba(234, 179, 8, 0.15)',
        };
      case 'SNAPSHOT_STALE_IDLE':
        if (!isConnected) {
          return {
            label: isZh ? '● 最后快照 (Runtime 已断开 · 数据已过期)' : '● Last Snapshot (Runtime Disconnected · Stale)',
            className: 'status-pill error',
            color: '#ef4444',
            bg: 'rgba(239, 68, 68, 0.15)',
          };
        }
        return {
          label: isZh ? '⏱ 等待回合操作' : '⏱ Waiting for Turn',
          className: 'status-pill info',
          color: '#06b6d4',
          bg: 'rgba(6, 182, 212, 0.15)',
        };
      case 'SESSION_EPOCH_CHANGED':
        return {
          label: isZh ? '↻ 会话已重置' : '↻ Epoch Reset',
          className: 'status-pill warning',
          color: '#f97316',
          bg: 'rgba(249, 115, 22, 0.15)',
        };
      case 'CONNECTED_NOT_SPECTATING':
        return {
          label: isZh
            ? userAccountInfo.status === 'IDENTITY_PENDING'
              ? '● Runtime 已连接 (等待账号识别)'
              : '● 已就绪 (等待进入观战)'
            : userAccountInfo.status === 'IDENTITY_PENDING'
            ? '● Runtime Connected (Identity Pending)'
            : '● Ready (Waiting for Room)',
          className: 'status-pill ok',
          color: '#22c55e',
          bg: 'rgba(34, 197, 94, 0.15)',
        };
      case 'DISCONNECTED':
      case 'NOT_CONNECTED':
      default:
        if (hasActiveRoom) {
          return {
            label: isZh ? '● 最后快照 (Runtime 已断开 · 数据已过期)' : '● Last Snapshot (Runtime Disconnected · Stale)',
            className: 'status-pill error',
            color: '#ef4444',
            bg: 'rgba(239, 68, 68, 0.15)',
          };
        }
        return {
          label: isZh ? '未连接 Runtime' : 'Disconnected',
          className: 'status-pill error',
          color: '#ef4444',
          bg: 'rgba(239, 68, 68, 0.15)',
        };
    }
  };

  const badgeInfo = getLifecycleBadge(lifecycleState);

  // Format 4 display slots (1, 2, 3, 4)
  const displaySlots: Array<{
    seatIndex: number;
    player: PlayerHandSummary | null;
  }> = [];

  if (hasActiveRoom) {
    for (let i = 0; i < 4; i++) {
      if (i < remotePlayerEntries.length) {
        displaySlots.push({
          seatIndex: i + 1,
          player: remotePlayerEntries[i][1],
        });
      } else {
        displaySlots.push({
          seatIndex: i + 1,
          player: null,
        });
      }
    }
  }

  // Handle Account Mode Selection
  const handleSelectAccountMode = (mode: 'USER_OWNED' | 'XINGSHU_MANAGED') => {
    setAccountMode(mode);
    setSelectorForceVisible(false);
  };

  const handleStepClick = (step: 1 | 2 | 3 | 4) => {
    if (step === 1) {
      setSelectorForceVisible((prev) => !prev);
    }
  };

  return (
    <div style={{ maxWidth: 1440, margin: '0 auto', paddingBottom: 40 }}>
      {/* 1. Header */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: 16,
          flexWrap: 'wrap',
          gap: 12,
        }}
      >
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <h1 style={{ fontSize: 22, fontWeight: 700, margin: 0, color: '#fff' }}>
              {isZh ? '全知观战助手' : 'Omniscient Spectator Assistant'}
            </h1>
            <span
              style={{
                fontSize: 11,
                padding: '2px 8px',
                borderRadius: 4,
                backgroundColor: 'rgba(99, 102, 241, 0.2)',
                color: 'var(--primary, #818cf8)',
                fontWeight: 600,
                border: '1px solid rgba(99, 102, 241, 0.4)',
              }}
            >
              HS4-P1.5
            </span>
          </div>
          <p style={{ color: 'var(--text-secondary, #94a3b8)', fontSize: 13, marginTop: 4, marginBottom: 0 }}>
            {isZh
              ? '四人全景实时手牌与局势透视 · 双客户端智能路由'
              : 'Real-time 4-player hand card display and tactical insight with multi-client routing'}
          </p>
        </div>

        {/* Dual-Client Routing Header Badges */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
          <div
            style={{
              padding: '6px 12px',
              borderRadius: 6,
              backgroundColor: spectatorSession ? 'rgba(99, 102, 241, 0.2)' : 'rgba(245, 158, 11, 0.15)',
              border: spectatorSession ? '1px solid rgba(99, 102, 241, 0.4)' : '1px solid rgba(245, 158, 11, 0.3)',
              fontSize: 12,
              display: 'flex',
              alignItems: 'center',
              gap: 6,
            }}
          >
            <span style={{ color: '#94a3b8' }}>{isZh ? '观战执行端：' : 'Spectator Runtime: '}</span>
            <strong style={{ color: spectatorSession ? '#a5b4fc' : '#fbbf24', fontFamily: 'monospace' }}>
              {spectatorSession ? `${spectatorPlatformName} · ${effectiveSpectatorId ?? (isZh ? '等待识别' : 'Pending')}` : (isZh ? `${spectatorPlatformName} · 未连接` : `${spectatorPlatformName} · Disconnected`)}
            </strong>
          </div>

          <div
            style={{
              padding: '6px 12px',
              borderRadius: 6,
              backgroundColor: mainSession ? 'rgba(34, 197, 94, 0.15)' : 'rgba(100, 116, 139, 0.15)',
              border: mainSession ? '1px solid rgba(34, 197, 94, 0.35)' : '1px solid rgba(100, 116, 139, 0.25)',
              fontSize: 12,
              display: 'flex',
              alignItems: 'center',
              gap: 6,
            }}
          >
            <span style={{ color: '#94a3b8' }}>{isZh ? '主游戏账号：' : 'Main Account: '}</span>
            <strong style={{ color: mainSession ? '#4ade80' : '#94a3b8', fontFamily: 'monospace' }}>
              {mainSession ? `Steam CN · ${effectiveMainId ?? (isZh ? '已连接' : 'Connected')}` : (isZh ? 'Steam CN · 未连接' : 'Steam CN · Disconnected')}
            </strong>
          </div>

          <button
            className="btn btn-secondary"
            onClick={manualRefresh}
            disabled={isRefreshing}
            style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '7px 14px' }}
          >
            <RefreshCw size={14} className={isRefreshing ? 'spin' : ''} />
            <span>{isZh ? '刷新状态' : 'Refresh'}</span>
          </button>
        </div>
      </div>

      {/* 2. Top Stepper (SpectatorStepIndicator) */}
      <SpectatorStepIndicator
        accountMode={accountMode}
        isConnected={isConnected}
        hasActiveRoom={hasActiveRoom}
        roomId={roomState.roomId}
        liveStreaming={liveStreaming}
        isSnapshotStale={isSnapshotStale}
        onSelectStep={handleStepClick}
        isZh={isZh}
      />

      {/* 2.1 HS4-P1.6 Auto-Follow Status Panel, Top-Level Cards & Diagnostics */}
      <AutoFollowStatusPanel
        autoFollowState={autoFollowState}
        autoFollowContext={autoFollowContext}
        mainSession={mainSession}
        spectatorSession={spectatorSession}
        onLaunchSpectatorClient={launchSpectatorClient}
        onRetryAutoFollow={retryAutoFollow}
        onManualJoin={manualJoinAutoFollow}
        onResetAutoFollow={resetAutoFollow}
        isZh={isZh}
      />

      {/* 2.2 Multi-Client Session Routing Card (Only rendered when not actively spectating room) */}
      {!hasActiveRoom && (
        <ClientSessionStatusCard
          sessions={resolvedSessions}
          selectedSpectatorConnectionId={selectedSpectatorConnectionId}
          spectatorPrompt={spectatorPrompt}
          accountMode={accountMode}
          onLaunchSpectatorClient={launchSpectatorClient}
          isZh={isZh}
        />
      )}

      {/* 2.5 Role Mismatch Warning Gate */}
      {hasRoleMismatch && (
        <div
          className="panel"
          style={{
            marginBottom: 20,
            padding: '16px 20px',
            borderRadius: 10,
            backgroundColor: 'rgba(239, 68, 68, 0.12)',
            border: '2px solid rgba(239, 68, 68, 0.55)',
            boxShadow: '0 0 20px rgba(239, 68, 68, 0.25)',
            display: 'flex',
            flexDirection: 'column',
            gap: 12,
            backdropFilter: 'blur(10px)',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'flex-start', gap: 14 }}>
            <div
              style={{
                width: 38,
                height: 38,
                borderRadius: 8,
                backgroundColor: 'rgba(239, 68, 68, 0.25)',
                color: '#ef4444',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
              }}
            >
              <AlertTriangle size={22} />
            </div>
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 15, fontWeight: 700, color: '#fca5a5' }}>
                {roleMismatchWarning || spectatorPrompt || (isZh ? '专用观战账号尚未启动。请启动已绑定的观战客户端并登录观战账号' : 'Dedicated spectator account not running. Please launch TapTap with spectator account')}
              </div>
              <div style={{ fontSize: 13, color: '#cbd5e1', marginTop: 4, lineHeight: 1.5 }}>
                {isZh
                  ? '安全防范规则：全知观战系统严禁使用主游戏账号直接进入观战房间，以防占用对局席位或干扰排位积分。当前主游戏账号 (Steam CN) 可保持在后台或前台正常运行，无需退出主账号！请直接启动已绑定的观战客户端进入观战房间。'
                  : 'Security Rule: Do NOT use your primary ladder account for spectating. Your primary account (Steam CN) can keep running! Launch TapTap client to spectate.'}
              </div>
            </div>
          </div>
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, flexWrap: 'wrap' }}>
            <button
              className="btn btn-secondary"
              onClick={launchSpectatorClient}
              style={{ fontSize: 12, padding: '6px 14px', display: 'flex', alignItems: 'center', gap: 6, color: '#38bdf8' }}
            >
              <Play size={14} />
              <span>{isZh ? `启动 ${spectatorPlatformName} 观战客户端` : `Launch ${spectatorPlatformName}`}</span>
            </button>
            <button
              className="btn btn-secondary"
              onClick={() => navigate('/player/accounts')}
              style={{ fontSize: 12, padding: '6px 14px', display: 'flex', alignItems: 'center', gap: 6 }}
            >
              <UserCheck size={14} />
              <span>{isZh ? '前往游戏账号中心查看绑定' : 'Go to Account Center'}</span>
            </button>
            <button
              className="btn btn-primary"
              onClick={() => handleSelectAccountMode('XINGSHU_MANAGED')}
              style={{ fontSize: 12, padding: '6px 14px' }}
            >
              <span>{isZh ? '查看星枢托管观战' : 'View Managed Spectating'}</span>
            </button>
          </div>
        </div>
      )}

      {/* 3. Active Account Banner (Collapsible top banner) */}
      {accountMode && (
        <div
          className="panel"
          style={{
            marginBottom: 20,
            padding: '12px 18px',
            backgroundColor:
              accountMode === 'USER_OWNED'
                ? 'rgba(17, 24, 39, 0.75)'
                : 'rgba(30, 27, 20, 0.85)',
            border:
              accountMode === 'USER_OWNED'
                ? '1px solid rgba(99, 102, 241, 0.35)'
                : '1px solid rgba(245, 158, 11, 0.4)',
            borderRadius: 10,
            backdropFilter: 'blur(10px)',
            transition: 'all 0.2s ease',
          }}
        >
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
              gap: 12,
            }}
          >
            {/* Left: Mode Title & Badges */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
              <div
                style={{
                  width: 32,
                  height: 32,
                  borderRadius: 8,
                  backgroundColor:
                    accountMode === 'USER_OWNED'
                      ? 'rgba(99, 102, 241, 0.2)'
                      : 'rgba(245, 158, 11, 0.2)',
                  color: accountMode === 'USER_OWNED' ? '#818cf8' : '#fbbf24',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                {accountMode === 'USER_OWNED' ? <UserCheck size={18} /> : <AlertTriangle size={18} />}
              </div>

              <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <span style={{ fontSize: 14, fontWeight: 700, color: '#fff' }}>
                    {accountMode === 'USER_OWNED'
                      ? isZh
                        ? '当前模式: 【我的观战账号】'
                        : 'Mode: 【My Spectator Account】'
                      : isZh
                      ? '当前模式: 【星枢托管观战账号】'
                      : 'Mode: 【XingShu Managed Account】'}
                  </span>
                  <span
                    style={{
                      fontSize: 11,
                      padding: '1px 6px',
                      borderRadius: 4,
                      backgroundColor:
                        accountMode === 'USER_OWNED'
                          ? 'rgba(99, 102, 241, 0.15)'
                          : 'rgba(245, 158, 11, 0.15)',
                      color: accountMode === 'USER_OWNED' ? '#a5b4fc' : '#fbbf24',
                      fontWeight: 600,
                      fontFamily: 'monospace',
                    }}
                  >
                    {accountMode}
                  </span>

                  {/* Top banner dual-routing identity pills */}
                  <span
                    style={{
                      fontSize: 11,
                      padding: '2px 8px',
                      borderRadius: 4,
                      backgroundColor: 'rgba(99, 102, 241, 0.2)',
                      color: '#a5b4fc',
                      fontWeight: 600,
                      border: '1px solid rgba(99, 102, 241, 0.4)',
                    }}
                  >
                    {spectatorSummaryText}
                  </span>
                  <span
                    style={{
                      fontSize: 11,
                      padding: '2px 8px',
                      borderRadius: 4,
                      backgroundColor: 'rgba(34, 197, 94, 0.15)',
                      color: '#4ade80',
                      fontWeight: 600,
                      border: '1px solid rgba(34, 197, 94, 0.35)',
                    }}
                  >
                    {mainSummaryText}
                  </span>
                  {accountMode === 'USER_OWNED' && (
                    <span
                      style={{
                        fontSize: 11,
                        padding: '1px 6px',
                        borderRadius: 4,
                        backgroundColor:
                          userAccountInfo.status === 'READY' || userAccountInfo.status === 'IDENTITY_PENDING'
                            ? 'rgba(34, 197, 94, 0.15)'
                            : userAccountInfo.status === 'IN_USE'
                            ? 'rgba(168, 85, 247, 0.15)'
                            : userAccountInfo.status === 'WAITING_RUNTIME'
                            ? 'rgba(56, 189, 248, 0.15)'
                            : 'rgba(148, 163, 184, 0.15)',
                        color:
                          userAccountInfo.status === 'READY' || userAccountInfo.status === 'IDENTITY_PENDING'
                            ? '#4ade80'
                            : userAccountInfo.status === 'IN_USE'
                            ? '#c084fc'
                            : userAccountInfo.status === 'WAITING_RUNTIME'
                            ? '#38bdf8'
                            : '#94a3b8',
                        fontWeight: 600,
                        fontFamily: 'monospace',
                      }}
                    >
                      {userAccountInfo.status === 'IDENTITY_PENDING'
                        ? (isZh ? '【Runtime 已连接】' : 'IDENTITY_PENDING')
                        : userAccountInfo.status === 'IN_USE'
                        ? (isZh ? '【对局观战中】' : 'IN_USE')
                        : userAccountInfo.status === 'READY'
                        ? (isZh ? '【已就绪】' : 'READY')
                        : userAccountInfo.status === 'WAITING_RUNTIME'
                        ? (isZh ? '【等待 Runtime 连接】' : 'WAITING_RUNTIME')
                        : userAccountInfo.status}
                    </span>
                  )}
                </div>

                <div
                  style={{
                    fontSize: 12,
                    color: 'var(--text-secondary, #94a3b8)',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    marginTop: 2,
                    flexWrap: 'wrap',
                  }}
                >
                  {accountMode === 'USER_OWNED' ? (
                    <>
                      <span>状态: <strong style={{
                        color:
                          userAccountInfo.status === 'READY' || userAccountInfo.status === 'IDENTITY_PENDING'
                            ? '#4ade80'
                            : userAccountInfo.status === 'IN_USE'
                            ? '#c084fc'
                            : userAccountInfo.status === 'WAITING_RUNTIME'
                            ? '#38bdf8'
                            : '#94a3b8'
                      }}>
                        {userAccountInfo.status === 'IDENTITY_PENDING'
                          ? (isZh ? 'Runtime 已连接 (等待账号识别)' : 'Runtime Connected (Identity Pending)')
                          : userAccountInfo.status === 'IN_USE'
                          ? (isZh ? '对局观战中' : 'In Match')
                          : userAccountInfo.status === 'READY'
                          ? (isZh ? '已就绪' : 'Ready')
                          : userAccountInfo.status === 'WAITING_RUNTIME'
                          ? (isZh ? '等待 Runtime 连接' : 'Waiting Runtime')
                          : (isZh ? '离线' : 'Offline')}
                      </strong></span>
                      <span>平台: <strong style={{ color: '#38bdf8' }}>{userAccountInfo.platformName || 'Steam 国服'}</strong></span>
                      <span>
                        PlayerId: <strong style={{ color: '#fff' }}>{formatSpectatorPlayerId(userAccountInfo.currentSessionPlayerId ?? spectatorPlayerId, userAccountInfo.runtimeConnected, isZh)}</strong>
                        {userAccountInfo.lastKnownPlayerId && !(userAccountInfo.currentSessionPlayerId ?? spectatorPlayerId) && (
                          <span style={{ fontSize: 11, color: '#64748b', marginLeft: 4 }}>
                            ({isZh ? `上次: ${userAccountInfo.lastKnownPlayerId}` : `Last: ${userAccountInfo.lastKnownPlayerId}`})
                          </span>
                        )}
                      </span>
                      <span>
                        Runtime:{' '}
                        <strong style={{ color: userAccountInfo.runtimeConnected ? '#22c55e' : '#f59e0b' }}>
                          {userAccountInfo.runtimeConnected ? (isZh ? '已就绪 (TCP: 25890)' : 'Connected') : (isZh ? '等待连接' : 'Disconnected')}
                        </strong>
                      </span>
                    </>
                  ) : (
                    <span style={{ color: managedAccountInfo.status === 'AVAILABLE' ? '#22c55e' : managedAccountInfo.status === 'ERROR' ? '#ef4444' : '#f59e0b', fontWeight: 600 }}>
                      {managedAccountInfo.status} — {managedAccountInfo.message}
                    </span>
                  )}
                </div>
              </div>
            </div>

            {/* Right: Actions */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              {accountMode === 'XINGSHU_MANAGED' && (
                <button
                  className="btn btn-primary"
                  onClick={() => handleSelectAccountMode('USER_OWNED')}
                  style={{ fontSize: 12, padding: '5px 12px' }}
                >
                  <span>{isZh ? '切换至我的账号' : 'Switch to My Account'}</span>
                </button>
              )}

              <button
                className="btn btn-secondary"
                onClick={() => navigate('/player/accounts')}
                style={{ fontSize: 12, padding: '5px 12px', display: 'flex', alignItems: 'center', gap: 5 }}
                title="前往游戏账号中心"
              >
                <UserCheck size={13} />
                <span>{isZh ? '账号中心' : 'Accounts'}</span>
              </button>

              <button
                className="btn btn-secondary"
                onClick={() => setSelectorForceVisible((prev) => !prev)}
                style={{ fontSize: 12, padding: '5px 12px', display: 'flex', alignItems: 'center', gap: 5 }}
              >
                <RotateCcw size={13} />
                <span>{selectorForceVisible ? (isZh ? '隐藏账号选择' : 'Hide Selector') : (isZh ? '切换账号模式' : 'Switch Mode')}</span>
              </button>

              <button
                onClick={() => setBannerExpanded((prev) => !prev)}
                style={{
                  background: 'none',
                  border: 'none',
                  color: 'var(--text-secondary, #94a3b8)',
                  cursor: 'pointer',
                  padding: 4,
                  display: 'flex',
                  alignItems: 'center',
                }}
                title={bannerExpanded ? (isZh ? '收起详情' : 'Collapse') : (isZh ? '展开详情' : 'Expand')}
              >
                {bannerExpanded ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
              </button>
            </div>
          </div>

          {/* Collapsible Info Details */}
          {bannerExpanded && (
            <div
              style={{
                marginTop: 10,
                paddingTop: 10,
                borderTop: '1px solid rgba(255, 255, 255, 0.08)',
                fontSize: 12,
                color: 'var(--text-secondary, #cbd5e1)',
                lineHeight: 1.5,
              }}
            >
              {accountMode === 'USER_OWNED' ? (
                <div>
                  {userAccountInfo.status === 'IDENTITY_PENDING'
                    ? isZh
                      ? '💡 Runtime 已连接，等待账号识别。请在官方游戏中输入任意正在进行的房间观战码，星枢进入观战房间后将自动读取当前账号并同步 4 名玩家全景手牌。'
                      : '💡 Runtime connected, waiting for account identity. Spectate any match in game, XingShu will automatically read the account and stream all 4 players hand cards.'
                    : userAccountInfo.status === 'IN_USE'
                    ? isZh
                      ? '💡 正在实时观战对局。星枢网桥正在同步 4 名玩家手牌与操作流。'
                      : '💡 Actively spectating match. XingShu bridge is synchronizing all 4 players hand cards.'
                    : isZh
                    ? '💡 已绑定当前本地客户端。在官方游戏中输入任意正在进行的房间观战码，星枢将自动开始捕获并呈现场上 4 名玩家的全景手牌。'
                    : '💡 Bound to local client. Join any ongoing room via spectate code in Astral Party to stream all 4 players hand cards in real-time.'}
                </div>
              ) : (
                <div style={{ color: managedAccountInfo.status === 'ERROR' ? '#fca5a5' : managedAccountInfo.status === 'AVAILABLE' ? '#bbf7d0' : '#fef3c7' }}>
                  {managedAccountInfo.message}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* 4. Account Selector: Rendered prominently when not in active match or when forced */}
      {(!hasActiveRoom || selectorForceVisible || accountMode === null) && (
        <SpectatorAccountSelector
          selectedMode={accountMode}
          onSelectMode={handleSelectAccountMode}
          isConnected={isConnected}
          isGameRunning={isGameRunning}
          gamePid={gamePid}
          playerId={spectatorPlayerId}
          userAccountInfo={userAccountInfo}
          managedAccountInfo={managedAccountInfo}
          onStartLocalValidation={startLocalManagedValidation}
          onAllocateSession={startLocalManagedValidation}
          onLaunchGame={launchGame}
          isZh={isZh}
        />
      )}

      {/* 5. Top Status Bar (Glassmorphism Dashboard Strip) */}
      <div
        className="panel"
        style={{
          padding: '12px 18px',
          marginBottom: 20,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 16,
          backgroundColor: 'rgba(17, 24, 39, 0.65)',
          backdropFilter: 'blur(10px)',
          border: '1px solid rgba(255, 255, 255, 0.08)',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 20, flexWrap: 'wrap' }}>
          {/* Transport Indicator */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Radio size={15} color={isConnected ? '#22c55e' : '#94a3b8'} />
            <span style={{ fontSize: 13, fontWeight: 600, color: isConnected ? '#fff' : 'var(--text-secondary)' }}>
              {isConnected
                ? isZh
                  ? '已连接 (Loopback TCP: 127.0.0.1:25890)'
                  : 'Connected (Loopback TCP: 127.0.0.1:25890)'
                : isZh
                ? '未连接 Runtime'
                : 'Disconnected'}
            </span>
          </div>

          {/* Platform Tag */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <ShieldCheck size={14} color="#38bdf8" />
            <span
              style={{
                fontSize: 12,
                backgroundColor: 'rgba(56, 189, 248, 0.12)',
                color: '#38bdf8',
                padding: '2px 8px',
                borderRadius: 4,
                fontWeight: 600,
              }}
            >
              {`${spectatorPlatformName} (VERIFIED)`}
            </span>
          </div>

          {/* Room ID Tag */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <Gamepad2 size={14} color="var(--accent, #a855f7)" />
            <span style={{ fontSize: 13 }}>
              {isZh ? '房间号: ' : 'Room ID: '}
              <strong style={{ color: roomState.roomId ? 'var(--accent, #a855f7)' : 'var(--text-muted, #64748b)' }}>
                {roomState.roomId ? roomState.roomId : isZh ? '未在对局' : 'None'}
              </strong>
            </span>
          </div>

          {/* Epoch & Sequence Tag */}
          {roomState.roomId && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--text-secondary)' }}>
              <Clock size={13} />
              <span>
                Epoch: <strong style={{ color: '#fff' }}>{roomState.epoch}</strong> | Seq:{' '}
                <strong style={{ color: '#fff' }}>#{roomState.sequenceNumber}</strong>
                {!isConnected && (
                  <span style={{ color: '#f87171', fontWeight: 600, marginLeft: 8 }}>
                    {isZh ? '[最后快照 · 数据已过期]' : '[Last Snapshot · Stale]'}
                  </span>
                )}
              </span>
            </div>
          )}
        </div>

        {/* Lifecycle Status Pill */}
        <div
          style={{
            fontSize: 12,
            fontWeight: 600,
            color: badgeInfo.color,
            backgroundColor: badgeInfo.bg,
            padding: '4px 12px',
            borderRadius: 6,
            border: `1px solid ${badgeInfo.color}40`,
            display: 'flex',
            alignItems: 'center',
            gap: 6,
          }}
        >
          {badgeInfo.label}
        </div>
      </div>

      {/* 6. Main Content Area: 4-Player Hand Card Grid */}
      {hasActiveRoom ? (
        <div
          id="spectator-hand-grid"
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(460px, 1fr))',
            gap: 16,
          }}
        >
          {displaySlots.map(({ seatIndex, player }) => {
            const displayCards: DisplayCardItem[] = player
              ? groupCardsForDisplay(player.cardIds)
              : [];

            return (
              <div
                key={seatIndex}
                className="panel"
                style={{
                  padding: 16,
                  display: 'flex',
                  flexDirection: 'column',
                  backgroundColor: 'rgba(17, 24, 39, 0.75)',
                  border: '1px solid rgba(255, 255, 255, 0.08)',
                  borderRadius: 10,
                  minHeight: 280,
                  transition: 'all 0.2s ease',
                }}
              >
                {/* Panel Header */}
                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    marginBottom: 14,
                    paddingBottom: 10,
                    borderBottom: '1px solid rgba(255, 255, 255, 0.08)',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                    <div
                      style={{
                        width: 28,
                        height: 28,
                        borderRadius: '50%',
                        backgroundColor: 'rgba(99, 102, 241, 0.2)',
                        color: 'var(--primary, #818cf8)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        fontWeight: 700,
                        fontSize: 13,
                      }}
                    >
                      {seatIndex}
                    </div>
                    <div>
                      <div style={{ fontSize: 15, fontWeight: 700, color: '#fff' }}>
                        {player ? (
                          <>
                            {isZh ? `玩家 ${player.playerId}` : `Player ${player.playerId}`}
                          </>
                        ) : (
                          <span style={{ color: 'var(--text-muted, #64748b)' }}>
                            {isZh ? `席位 ${seatIndex} (空)` : `Seat ${seatIndex} (Empty)`}
                          </span>
                        )}
                      </div>
                      <div style={{ fontSize: 11, color: 'var(--text-secondary, #94a3b8)' }}>
                        {isZh ? `远端对局玩家 · 席位 ${seatIndex}` : `Remote Player · Seat ${seatIndex}`}
                      </div>
                    </div>
                  </div>

                  {player && (
                    <div
                      style={{
                        fontSize: 12,
                        fontWeight: 700,
                        padding: '4px 10px',
                        borderRadius: 20,
                        backgroundColor:
                          !isConnected
                            ? 'rgba(239, 68, 68, 0.15)'
                            : player.handSize > 0
                            ? 'rgba(34, 197, 94, 0.15)'
                            : 'rgba(148, 163, 184, 0.12)',
                        color:
                          !isConnected
                            ? '#f87171'
                            : player.handSize > 0
                            ? '#4ade80'
                            : 'var(--text-muted, #64748b)',
                        border:
                          !isConnected
                            ? '1px solid rgba(239, 68, 68, 0.35)'
                            : player.handSize > 0
                            ? '1px solid rgba(34, 197, 94, 0.3)'
                            : '1px solid rgba(255, 255, 255, 0.08)',
                      }}
                    >
                      {!isConnected
                        ? isZh
                          ? `${player.handSize} 张手牌 (快照已过期)`
                          : `${player.handSize} Cards (Stale Snapshot)`
                        : isZh
                        ? `${player.handSize} 张手牌`
                        : `${player.handSize} Cards`}
                    </div>
                  )}
                </div>

                {/* Hand Cards Grid */}
                <div style={{ flex: 1 }}>
                  {player && displayCards.length > 0 ? (
                    <div
                      style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fill, minmax(135px, 1fr))',
                        gap: 10,
                      }}
                    >
                      {displayCards.map((item) => {
                        const badgeStyle = getCardCategoryBadgeStyle(item.card.category);
                        return (
                          <div
                            key={item.cardId}
                            style={{
                              backgroundColor: 'rgba(255, 255, 255, 0.03)',
                              border: '1px solid rgba(255, 255, 255, 0.08)',
                              borderRadius: 8,
                              padding: 8,
                              display: 'flex',
                              flexDirection: 'column',
                              position: 'relative',
                              overflow: 'hidden',
                              transition: 'transform 0.15s ease, border-color 0.15s ease',
                            }}
                          >
                            {/* Duplicate Count Badge (Top-right pill) */}
                            {item.count > 1 && (
                              <div
                                style={{
                                  position: 'absolute',
                                  top: 6,
                                  right: 6,
                                  backgroundColor: '#ef4444',
                                  color: '#fff',
                                  fontSize: 10,
                                  fontWeight: 800,
                                  padding: '1px 6px',
                                  borderRadius: 10,
                                  boxShadow: '0 2px 4px rgba(0,0,0,0.5)',
                                  zIndex: 2,
                                }}
                              >
                                x{item.count}
                              </div>
                            )}

                            {/* Card Art Thumbnail */}
                            <div
                              style={{
                                width: '100%',
                                height: 72,
                                borderRadius: 6,
                                overflow: 'hidden',
                                backgroundColor: 'rgba(0, 0, 0, 0.35)',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                marginBottom: 8,
                              }}
                            >
                              <img
                                src={item.card.icon}
                                alt={item.card.name}
                                style={{
                                  width: '100%',
                                  height: '100%',
                                  objectFit: 'contain',
                                }}
                                onError={(e) => {
                                  // Fallback to uniform grey card fallback on load error
                                  (e.currentTarget as HTMLImageElement).src =
                                    '/cards/card_fallback.svg';
                                }}
                              />
                            </div>

                            {/* Card Name */}
                            <div
                              style={{
                                fontSize: 13,
                                fontWeight: 700,
                                color: '#fff',
                                whiteSpace: 'nowrap',
                                overflow: 'hidden',
                                textOverflow: 'ellipsis',
                                marginBottom: 4,
                              }}
                              title={item.card.name}
                            >
                              {item.card.name}
                            </div>

                            {/* Metadata Badges */}
                            <div
                              style={{
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'space-between',
                                fontSize: 10,
                                marginTop: 'auto',
                              }}
                            >
                              <span
                                style={{
                                  padding: '1px 5px',
                                  borderRadius: 3,
                                  backgroundColor: badgeStyle.bg,
                                  color: badgeStyle.color,
                                  border: `1px solid ${badgeStyle.border}`,
                                  fontWeight: 600,
                                }}
                              >
                                {item.card.category}
                              </span>

                              {item.card.cost !== undefined && item.card.cost !== null && (
                                <span style={{ color: '#fbbf24', fontWeight: 600 }}>
                                  Cost {item.card.cost}
                                </span>
                              )}
                            </div>

                            {/* Subtle Card ID subtext */}
                            <div
                              style={{
                                fontSize: 9,
                                color: 'rgba(255, 255, 255, 0.3)',
                                marginTop: 4,
                                textAlign: 'right',
                              }}
                            >
                              ID: {item.cardId}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  ) : player && player.handSize === 0 ? (
                    <div
                      style={{
                        height: 160,
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'center',
                        justifyContent: 'center',
                        color: 'var(--text-muted, #64748b)',
                        gap: 8,
                      }}
                    >
                      <Layers size={28} strokeWidth={1.5} />
                      <span style={{ fontSize: 13 }}>
                        {isZh ? '该玩家手牌为空' : 'Hand is empty'}
                      </span>
                    </div>
                  ) : (
                    <div
                      style={{
                        height: 160,
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'center',
                        justifyContent: 'center',
                        color: 'var(--text-muted, #64748b)',
                        gap: 8,
                      }}
                    >
                      <Users size={28} strokeWidth={1.5} />
                      <span style={{ fontSize: 13 }}>
                        {isZh ? '等待玩家进入席位...' : 'Waiting for player...'}
                      </span>
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        /* Empty / Waiting Room Placeholder (when account selected and waiting for match) */
        accountMode && (
          <div
            className="panel"
            style={{
              padding: '40px 24px',
              textAlign: 'center',
              backgroundColor: 'rgba(17, 24, 39, 0.65)',
              border: '1px solid rgba(255, 255, 255, 0.08)',
              borderRadius: 12,
            }}
          >
            <div
              style={{
                width: 52,
                height: 52,
                borderRadius: '50%',
                backgroundColor: 'rgba(99, 102, 241, 0.15)',
                color: 'var(--primary, #818cf8)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                margin: '0 auto 16px auto',
              }}
            >
              <Eye size={26} />
            </div>
            <h2 style={{ fontSize: 18, fontWeight: 700, color: '#fff', marginBottom: 8 }}>
              {isZh ? '等待接入观战对局' : 'Waiting for Spectator Match'}
            </h2>
            <p
              style={{
                fontSize: 13,
                color: 'var(--text-secondary, #94a3b8)',
                maxWidth: 540,
                margin: '0 auto 20px auto',
                lineHeight: 1.6,
              }}
            >
              {isZh
                ? '请在游戏客户端中输入房间观战码进入任意正在进行的对局。星枢网桥将通过 Loopback TCP 实时捕获服务端授权广播的 4 名玩家全景手牌，并在此处自动呈现实时卡牌列表。'
                : 'Join any in-progress room as a spectator inside Astral Party. XingShu Bridge will stream the authorized 4-player hand snapshots here in real-time.'}
            </p>

            <div
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 8,
                padding: '8px 16px',
                borderRadius: 6,
                backgroundColor: 'rgba(255, 255, 255, 0.03)',
                border: '1px solid rgba(255, 255, 255, 0.08)',
                fontSize: 12,
                color: 'var(--text-secondary, #94a3b8)',
              }}
            >
              <Sparkles size={14} color="var(--accent, #a855f7)" />
              <span>
                {isZh
                  ? '已就绪：支持 1920×1080 及 1366×768 响应式自适应布局'
                  : 'Ready: Fully responsive for 1920x1080 and 1366x768 screens'}
              </span>
            </div>
          </div>
        )
      )}
    </div>
  );
};
