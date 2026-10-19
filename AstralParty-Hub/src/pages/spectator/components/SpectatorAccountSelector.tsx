import React from 'react';
import {
  User,
  Cloud,
  ShieldCheck,
  Gamepad2,
  Radio,
  AlertTriangle,
  Play,
  CheckCircle2,
  ExternalLink,
  Info,
  Clock,
  Sparkles,
  ServerOff,
  RotateCcw,
} from 'lucide-react';
import type { SpectatorAccountMode } from '../useSpectatorState';
import type { UserOwnedAccountInfo, ManagedAccountInfo, SpectatorAccountStatus } from '../../../types/spectatorAccount';
import { formatSpectatorPlayerId } from '../spectatorTransition';

export interface SpectatorAccountSelectorProps {
  selectedMode: SpectatorAccountMode;
  onSelectMode: (mode: 'USER_OWNED' | 'XINGSHU_MANAGED') => void;
  isConnected?: boolean;
  isGameRunning?: boolean;
  gamePid?: number;
  playerId?: number | null;
  userAccountInfo?: UserOwnedAccountInfo;
  managedAccountInfo?: ManagedAccountInfo;
  onLaunchGame?: () => void;
  onAllocateSession?: () => void;
  onStartLocalValidation?: () => void;
  isZh?: boolean;
}

export const SpectatorAccountSelector: React.FC<SpectatorAccountSelectorProps> = ({
  selectedMode,
  onSelectMode,
  isConnected,
  isGameRunning,
  gamePid,
  playerId,
  userAccountInfo,
  managedAccountInfo,
  onLaunchGame,
  onAllocateSession,
  onStartLocalValidation,
  isZh = true,
}) => {
  const isUserOwnedActive = selectedMode === 'USER_OWNED';
  const isManagedActive = selectedMode === 'XINGSHU_MANAGED';

  // Resolve user account metrics from userAccountInfo or fallbacks
  const effectiveGameRunning = userAccountInfo ? userAccountInfo.gameRunning : (isGameRunning ?? false);
  const effectiveConnected = userAccountInfo ? userAccountInfo.runtimeConnected : (isConnected ?? false);
  const effectivePlayerId = userAccountInfo?.currentSessionPlayerId ?? (effectiveConnected && playerId ? playerId : null);
  const userAccountStatus: SpectatorAccountStatus = userAccountInfo?.status ?? (
    !effectiveGameRunning
      ? 'OFFLINE'
      : !effectiveConnected
      ? 'WAITING_RUNTIME'
      : effectivePlayerId
      ? 'READY'
      : 'IDENTITY_PENDING'
  );

  const getUserStatusVisuals = () => {
    switch (userAccountStatus) {
      case 'READY':
        return {
          color: '#22c55e',
          bg: 'rgba(34, 197, 94, 0.15)',
          text: isZh ? '就绪 (READY)' : 'READY',
          badgeText: isZh ? '【就绪】' : '【READY】',
        };
      case 'IN_USE':
        return {
          color: '#a855f7',
          bg: 'rgba(168, 85, 247, 0.15)',
          text: isZh ? '对局中 (IN_USE)' : 'IN_USE',
          badgeText: isZh ? '【对局观战中】' : '【IN_USE】',
        };
      case 'IDENTITY_PENDING':
        return {
          color: '#22c55e',
          bg: 'rgba(34, 197, 94, 0.15)',
          text: isZh ? 'Runtime 已连接' : 'IDENTITY_PENDING',
          badgeText: isZh ? '【Runtime 已连接】' : '【RUNTIME CONNECTED】',
        };
      case 'WAITING_RUNTIME':
        return {
          color: '#38bdf8',
          bg: 'rgba(56, 189, 248, 0.15)',
          text: isZh ? '等待 Runtime 连接' : 'WAITING_RUNTIME',
          badgeText: isZh ? '【等待 Runtime 连接】' : '【WAITING RUNTIME】',
        };
      case 'LOGIN_REQUIRED':
        return {
          color: '#f59e0b',
          bg: 'rgba(245, 158, 11, 0.15)',
          text: isZh ? '等待登录 (LOGIN_REQUIRED)' : 'LOGIN_REQUIRED',
          badgeText: isZh ? '【等待登录】' : '【LOGIN REQUIRED】',
        };
      case 'OFFLINE':
      default:
        return {
          color: '#94a3b8',
          bg: 'rgba(148, 163, 184, 0.15)',
          text: isZh ? '离线 (OFFLINE)' : 'OFFLINE',
          badgeText: isZh ? '【离线】' : '【OFFLINE】',
        };
    }
  };
  const userVisuals = getUserStatusVisuals();

  // Resolve managed account metrics dynamically
  const managedStatus = managedAccountInfo?.status ?? 'SERVICE_NOT_CONFIGURED';
  const isLocalValidation = managedAccountInfo?.executionMode === 'LOCAL_VALIDATION';
  const managedMessage =
    managedAccountInfo?.message ||
    (isZh ? '星枢官方远程观战账号池服务状态待更新。' : 'Spectator pool service status pending.');

  const isManagedAvailable = managedStatus === 'AVAILABLE';
  const isManagedError = managedStatus === 'ERROR';
  const isManagedUnconfigured = managedStatus === 'SERVICE_NOT_CONFIGURED' || managedStatus === 'UNCONFIGURED';

  const getManagedStatusVisuals = () => {
    if (isManagedAvailable) {
      return {
        badgeText: isZh ? '可用 (随时可分配)' : 'Available',
        badgeColor: '#22c55e',
        badgeBg: 'rgba(34, 197, 94, 0.15)',
        badgeBorder: 'rgba(34, 197, 94, 0.35)',
        cardBorder: isManagedActive ? '2px solid rgba(34, 197, 94, 0.7)' : '1px solid rgba(255, 255, 255, 0.1)',
        cardGlow: isManagedActive ? '0 0 24px rgba(34, 197, 94, 0.2)' : '0 4px 12px rgba(0, 0, 0, 0.2)',
        bannerBg: 'rgba(34, 197, 94, 0.12)',
        bannerBorder: 'rgba(34, 197, 94, 0.35)',
        bannerText: '#bbf7d0',
        bannerHeaderColor: '#4ade80',
        headerIcon: <CheckCircle2 size={16} />,
        headerTitle: isZh ? '可用 (随时可分配)' : 'Available (Ready to Allocate)',
      };
    }

    if (isManagedError) {
      return {
        badgeText: isZh ? '服务异常' : 'Service Error',
        badgeColor: '#ef4444',
        badgeBg: 'rgba(239, 68, 68, 0.15)',
        badgeBorder: 'rgba(239, 68, 68, 0.35)',
        cardBorder: isManagedActive ? '2px solid rgba(239, 68, 68, 0.7)' : '1px solid rgba(255, 255, 255, 0.1)',
        cardGlow: isManagedActive ? '0 0 24px rgba(239, 68, 68, 0.2)' : '0 4px 12px rgba(0, 0, 0, 0.2)',
        bannerBg: 'rgba(239, 68, 68, 0.12)',
        bannerBorder: 'rgba(239, 68, 68, 0.35)',
        bannerText: '#fecaca',
        bannerHeaderColor: '#f87171',
        headerIcon: <ServerOff size={16} />,
        headerTitle: isZh ? '服务异常' : 'Service Error',
      };
    }

    if (isManagedUnconfigured) {
      return {
        badgeText: isZh ? '服务未配置' : 'Service Not Configured',
        badgeColor: '#f59e0b',
        badgeBg: 'rgba(245, 158, 11, 0.15)',
        badgeBorder: 'rgba(245, 158, 11, 0.35)',
        cardBorder: isManagedActive ? '2px solid rgba(245, 158, 11, 0.7)' : '1px solid rgba(255, 255, 255, 0.1)',
        cardGlow: isManagedActive ? '0 0 24px rgba(245, 158, 11, 0.2)' : '0 4px 12px rgba(0, 0, 0, 0.2)',
        bannerBg: 'rgba(245, 158, 11, 0.12)',
        bannerBorder: 'rgba(245, 158, 11, 0.35)',
        bannerText: '#fef3c7',
        bannerHeaderColor: '#fbbf24',
        headerIcon: <AlertTriangle size={16} />,
        headerTitle: isZh ? '服务未配置' : 'Service Not Configured',
      };
    }

    // Dynamic fallback for any other status (e.g. ALLOCATING, READY, IN_USE)
    return {
      badgeText: managedStatus,
      badgeColor: '#38bdf8',
      badgeBg: 'rgba(56, 189, 248, 0.15)',
      badgeBorder: 'rgba(56, 189, 248, 0.35)',
      cardBorder: isManagedActive ? '2px solid rgba(56, 189, 248, 0.7)' : '1px solid rgba(255, 255, 255, 0.1)',
      cardGlow: isManagedActive ? '0 0 24px rgba(56, 189, 248, 0.2)' : '0 4px 12px rgba(0, 0, 0, 0.2)',
      bannerBg: 'rgba(56, 189, 248, 0.12)',
      bannerBorder: 'rgba(56, 189, 248, 0.35)',
      bannerText: '#e0f2fe',
      bannerHeaderColor: '#38bdf8',
      headerIcon: <Info size={16} />,
      headerTitle: managedStatus,
    };
  };

  const managedVisuals = getManagedStatusVisuals();

  return (
    <div style={{ marginBottom: 24 }}>
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(420px, 1fr))',
          gap: 20,
        }}
      >
        {/* CARD A: 【我的观战账号】 (USER_OWNED) */}
        <div
          className="panel spectator-card-user-owned"
          style={{
            padding: 24,
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
            backgroundColor: isUserOwnedActive
              ? 'rgba(17, 24, 39, 0.85)'
              : 'rgba(17, 24, 39, 0.65)',
            border: isUserOwnedActive
              ? '2px solid rgba(99, 102, 241, 0.7)'
              : '1px solid rgba(255, 255, 255, 0.1)',
            borderRadius: 12,
            boxShadow: isUserOwnedActive
              ? '0 0 24px rgba(99, 102, 241, 0.2)'
              : '0 4px 12px rgba(0, 0, 0, 0.2)',
            transition: 'all 0.2s ease',
            position: 'relative',
            overflow: 'hidden',
          }}
        >
          {/* Active Accent Tag */}
          {isUserOwnedActive && (
            <div
              style={{
                position: 'absolute',
                top: 0,
                right: 0,
                backgroundColor: 'var(--primary, #6366f1)',
                color: '#fff',
                fontSize: 11,
                fontWeight: 700,
                padding: '4px 14px',
                borderBottomLeftRadius: 10,
                display: 'flex',
                alignItems: 'center',
                gap: 5,
              }}
            >
              <CheckCircle2 size={13} />
              <span>{isZh ? '当前选定模式' : 'Active Mode'}</span>
            </div>
          )}

          <div>
            {/* Header */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
              <div
                style={{
                  width: 44,
                  height: 44,
                  borderRadius: 10,
                  backgroundColor: 'rgba(99, 102, 241, 0.15)',
                  color: 'var(--primary, #818cf8)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                <User size={24} />
              </div>
              <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                  <h3 style={{ fontSize: 18, fontWeight: 700, margin: 0, color: '#fff' }}>
                    {isZh ? '【我的观战账号】' : '【My Spectator Account】'}
                  </h3>
                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 700,
                      color: '#a5b4fc',
                      backgroundColor: 'rgba(99, 102, 241, 0.15)',
                      padding: '2px 8px',
                      borderRadius: 4,
                      fontFamily: 'monospace',
                    }}
                  >
                    USER_OWNED
                  </span>
                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 700,
                      color: userVisuals.color,
                      backgroundColor: userVisuals.bg,
                      padding: '2px 8px',
                      borderRadius: 4,
                      fontFamily: 'monospace',
                      border: `1px solid ${userVisuals.color}40`,
                    }}
                  >
                    {userAccountStatus === 'IDENTITY_PENDING'
                      ? (isZh ? '【Runtime 已连接】' : 'IDENTITY_PENDING')
                      : userAccountStatus === 'IN_USE'
                      ? (isZh ? '【对局观战中】' : 'IN_USE')
                      : userAccountStatus}
                  </span>
                </div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)', marginTop: 2 }}>
                  {userAccountStatus === 'IDENTITY_PENDING'
                    ? (isZh
                        ? '等待账号识别 (进入观战房间后将自动读取当前账号)'
                        : 'Waiting for identity (Account will be read automatically on entering room)')
                    : isZh
                    ? '推荐模式 · 基于当前官方客户端及本地网桥'
                    : 'Recommended · Local Client via XingShu Bridge'}
                </div>
              </div>
            </div>

            {/* Explanation */}
            <p
              style={{
                fontSize: 13,
                lineHeight: 1.6,
                color: 'var(--text-secondary, #cbd5e1)',
                marginTop: 8,
                marginBottom: 16,
              }}
            >
              {isZh
                ? '使用你当前登录的 Astral Party 官方客户端进入观战，星枢通过本地网桥实时同步远端玩家手牌。'
                : 'Spectate via your locally logged-in Astral Party official client. XingShu synchronizes remote players hands via local bridge in real-time.'}
            </p>

            {/* Badges Strip: Platform, Account Status, Game Status, Runtime Status, PlayerId */}
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(2, 1fr)',
                gap: 8,
                marginBottom: 16,
              }}
            >
              {/* 1. Platform */}
              <div
                style={{
                  padding: '7px 10px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid rgba(255, 255, 255, 0.07)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                <ShieldCheck size={14} color="#38bdf8" />
                <div>
                  <div style={{ fontSize: 10, color: '#94a3b8' }}>{isZh ? '运行平台' : 'Platform'}</div>
                  <div style={{ fontSize: 12, fontWeight: 600, color: '#38bdf8' }}>
                    {userAccountInfo?.platformName || (isZh ? 'TapTap 客户端 (专用观战)' : 'TapTap (Dedicated Spectator)')}
                  </div>
                </div>
              </div>

              {/* 2. Account Status */}
              <div
                style={{
                  padding: '7px 10px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid rgba(255, 255, 255, 0.07)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                <User size={14} color={userVisuals.color} />
                <div>
                  <div style={{ fontSize: 10, color: '#94a3b8' }}>{isZh ? '账号状态' : 'Status'}</div>
                  <div style={{ fontSize: 12, fontWeight: 700, color: userVisuals.color }}>
                    {userAccountStatus === 'IDENTITY_PENDING'
                      ? (isZh ? '【Runtime 已连接】' : 'IDENTITY_PENDING')
                      : userAccountStatus === 'IN_USE'
                      ? (isZh ? '【对局观战中】' : 'IN_USE')
                      : userVisuals.text}
                  </div>
                </div>
              </div>

              {/* 3. Game Status */}
              <div
                style={{
                  padding: '7px 10px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid rgba(255, 255, 255, 0.07)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                <Gamepad2 size={14} color={effectiveGameRunning ? '#22c55e' : '#f59e0b'} />
                <div>
                  <div style={{ fontSize: 10, color: '#94a3b8' }}>{isZh ? '游戏状态' : 'Game Status'}</div>
                  <div
                    style={{
                      fontSize: 12,
                      fontWeight: 600,
                      color: effectiveGameRunning ? '#22c55e' : '#f59e0b',
                    }}
                  >
                    {effectiveGameRunning
                      ? isZh
                        ? `游戏运行中 ${gamePid ? `(PID: ${gamePid})` : ''}`
                        : 'Game Running'
                      : isZh
                      ? '游戏未运行'
                      : 'Not Running'}
                  </div>
                </div>
              </div>

              {/* 4. Runtime Status */}
              <div
                style={{
                  padding: '7px 10px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid rgba(255, 255, 255, 0.07)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                <Radio size={14} color={effectiveConnected ? '#22c55e' : '#94a3b8'} />
                <div>
                  <div style={{ fontSize: 10, color: '#94a3b8' }}>{isZh ? 'Runtime 状态' : 'Runtime'}</div>
                  <div
                    style={{
                      fontSize: 12,
                      fontWeight: 600,
                      color: effectiveConnected ? '#22c55e' : '#94a3b8',
                    }}
                  >
                    {effectiveConnected
                      ? isZh
                        ? 'Runtime 已连接 (25890)'
                        : 'Runtime Connected'
                      : isZh
                      ? '等待连接...'
                      : 'Disconnected'}
                  </div>
                </div>
              </div>

              {/* 5. PlayerId */}
              <div
                style={{
                  padding: '7px 10px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid rgba(255, 255, 255, 0.07)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  gridColumn: 'span 2',
                }}
              >
                <User size={14} color="#818cf8" />
                <div>
                  <div style={{ fontSize: 10, color: '#94a3b8' }}>{isZh ? '当前账号 ID' : 'Player ID'}</div>
                  <div style={{ fontSize: 12, fontWeight: 700, color: '#fff', fontFamily: 'monospace' }}>
                    {formatSpectatorPlayerId(effectivePlayerId, effectiveConnected || effectiveGameRunning, isZh)}
                  </div>
                  {userAccountInfo?.lastKnownPlayerId && !effectivePlayerId && (
                    <div style={{ fontSize: 10, color: '#64748b', marginTop: 2 }}>
                      {isZh ? `上次识别账号: ${userAccountInfo.lastKnownPlayerId}` : `Last: ${userAccountInfo.lastKnownPlayerId}`}
                    </div>
                  )}
                </div>
              </div>
            </div>

            {/* Prompt when spectator game not running or runtime not connected */}
            {!effectiveGameRunning && (
              <div
                style={{
                  padding: '8px 12px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(245, 158, 11, 0.1)',
                  border: '1px solid rgba(245, 158, 11, 0.25)',
                  color: '#fbbf24',
                  fontSize: 12,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  marginBottom: 14,
                }}
              >
                <Info size={14} />
                <span>
                  {isZh
                    ? '专用观战账号尚未启动。请启动已绑定平台的客户端并登录观战账号。'
                    : 'Dedicated spectator account not running. Please launch TapTap with spectator account.'}
                </span>
              </div>
            )}

            {effectiveGameRunning && !effectiveConnected && (
              <div
                style={{
                  padding: '8px 12px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(56, 189, 248, 0.1)',
                  border: '1px solid rgba(56, 189, 248, 0.25)',
                  color: '#38bdf8',
                  fontSize: 12,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  marginBottom: 14,
                }}
              >
                <Clock size={14} className="spin" />
                <span>
                  {isZh
                    ? '等待星枢 Runtime 连接...'
                    : 'Waiting for XingShu Runtime connection...'}
                </span>
              </div>
            )}

            {effectiveGameRunning && effectiveConnected && userAccountStatus === 'IDENTITY_PENDING' && (
              <div
                style={{
                  padding: '8px 12px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(34, 197, 94, 0.1)',
                  border: '1px solid rgba(34, 197, 94, 0.25)',
                  color: '#4ade80',
                  fontSize: 12,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  marginBottom: 14,
                }}
              >
                <CheckCircle2 size={14} />
                <span>
                  {isZh
                    ? 'Runtime 已连接，等待账号识别 (进入观战房间后将自动读取当前账号)'
                    : 'Runtime connected, waiting for identity (Account will be read automatically on entering room)'}
                </span>
              </div>
            )}
          </div>

          {/* Action Buttons */}
          <div style={{ display: 'flex', gap: 10, marginTop: 8, flexWrap: 'wrap' }}>
            <button
              className="btn btn-primary"
              onClick={() => onSelectMode('USER_OWNED')}
              style={{
                flex: 1,
                minWidth: 160,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: 8,
                padding: '9px 16px',
                fontWeight: 600,
              }}
            >
              <CheckCircle2 size={16} />
              <span>
                {isUserOwnedActive
                  ? isZh
                    ? userAccountStatus === 'IDENTITY_PENDING'
                      ? '使用当前账号 (待识别)'
                      : userAccountStatus === 'READY'
                      ? '已就绪'
                      : '当前使用中 (我的账号)'
                    : userAccountStatus === 'IDENTITY_PENDING'
                    ? 'Use Current Account (Pending)'
                    : userAccountStatus === 'READY'
                    ? 'Ready'
                    : 'Active (My Account)'
                  : isZh
                  ? userAccountStatus === 'IDENTITY_PENDING'
                    ? '使用当前账号 (待识别)'
                    : userAccountStatus === 'READY'
                    ? '已就绪'
                    : '使用我的账号'
                  : 'Use My Account'}
              </span>
            </button>

            {effectiveGameRunning ? (
              <button
                className="btn btn-secondary"
                disabled
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  padding: '9px 14px',
                  color: '#94a3b8',
                  opacity: 0.65,
                  cursor: 'not-allowed',
                }}
                title={isZh ? '游戏客户端正在运行中' : 'Game client is already running'}
              >
                <CheckCircle2 size={14} color="#22c55e" />
                <span>{isZh ? '游戏已运行' : 'Game Running'}</span>
              </button>
            ) : (
              <button
                className="btn btn-secondary"
                onClick={onLaunchGame}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  padding: '9px 14px',
                  color: '#38bdf8',
                }}
                title={isZh ? '启动已绑定的专用观战客户端' : 'Launch Dedicated TapTap Spectator Client'}
              >
                <Play size={14} />
                <span>{isZh ? '启动已绑定观战端' : 'Launch TapTap'}</span>
              </button>
            )}
          </div>

        </div>

        {/* CARD B: 【星枢官方观战账号】 (XINGSHU_MANAGED) */}
        <div
          className="panel spectator-card-managed"
          style={{
            padding: 24,
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
            backgroundColor: isManagedActive
              ? 'rgba(17, 24, 39, 0.85)'
              : 'rgba(17, 24, 39, 0.65)',
            border: managedVisuals.cardBorder,
            borderRadius: 12,
            boxShadow: managedVisuals.cardGlow,
            transition: 'all 0.2s ease',
            position: 'relative',
            overflow: 'hidden',
          }}
        >
          {/* Active Status Tag */}
          {isManagedActive && (
            <div
              style={{
                position: 'absolute',
                top: 0,
                right: 0,
                backgroundColor: managedVisuals.badgeColor,
                color: isManagedError ? '#fff' : '#000',
                fontSize: 11,
                fontWeight: 700,
                padding: '4px 14px',
                borderBottomLeftRadius: 10,
                display: 'flex',
                alignItems: 'center',
                gap: 5,
              }}
            >
              {managedVisuals.headerIcon}
              <span>{managedVisuals.badgeText}</span>
            </div>
          )}

          <div>
            {/* Header */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
              <div
                style={{
                  width: 44,
                  height: 44,
                  borderRadius: 10,
                  backgroundColor: managedVisuals.badgeBg,
                  color: managedVisuals.badgeColor,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                <Cloud size={24} />
              </div>
              <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                  <h3 style={{ fontSize: 18, fontWeight: 700, margin: 0, color: '#fff' }}>
                    {isZh ? '【星枢托管观战账号】' : '【XingShu Managed Account】'}
                  </h3>
                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 700,
                      color: managedVisuals.badgeColor,
                      backgroundColor: managedVisuals.badgeBg,
                      padding: '2px 8px',
                      borderRadius: 4,
                      fontFamily: 'monospace',
                    }}
                  >
                    XINGSHU_MANAGED
                  </span>
                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 700,
                      color: managedVisuals.badgeColor,
                      backgroundColor: managedVisuals.badgeBg,
                      padding: '2px 8px',
                      borderRadius: 4,
                      fontFamily: 'monospace',
                      border: `1px solid ${managedVisuals.badgeBorder}`,
                    }}
                  >
                    {managedStatus}
                  </span>
                </div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)', marginTop: 2 }}>
                  {isLocalValidation
                    ? (isZh ? '本机验证 · 使用已绑定平台的官方客户端' : 'Local validation · Bound TapTap official client')
                    : (isZh ? '独立服务托管 · 当前可先在本机验证' : 'Managed service · Local validation available')}
                </div>
              </div>
            </div>

            {/* Explanation */}
            <p
              style={{
                fontSize: 13,
                lineHeight: 1.6,
                color: 'var(--text-secondary, #cbd5e1)',
                marginTop: 8,
                marginBottom: 16,
              }}
            >
              {isZh
                ? '先在本机验证账号租用、自动跟房和真实手牌同步。使用已绑定的观战账号，仍需保持官方客户端运行；独立服务器账号池尚未上线。'
                : 'Validate account leasing, automatic room following and real hand streaming locally. Keep the bound TapTap official client running. The independent server pool is not live yet.'}
            </p>

            {/* Badges Strip */}
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(2, 1fr)',
                gap: 8,
                marginBottom: 16,
              }}
            >
              {/* Badge 1: 服务类别 */}
              <div
                style={{
                  padding: '7px 10px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid rgba(255, 255, 255, 0.07)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                <Sparkles size={14} color="#818cf8" />
                <div>
                  <div style={{ fontSize: 10, color: '#94a3b8' }}>{isZh ? '服务类别' : 'Service'}</div>
                  <div style={{ fontSize: 12, fontWeight: 600, color: '#818cf8' }}>
                    {isLocalValidation ? (isZh ? '本机验证 Worker' : 'Local validation worker') : (isZh ? '独立服务待接入' : 'Managed service pending')}
                  </div>
                </div>
              </div>

              {/* Badge 2: 配置状态 (动态呈现 managedAccountInfo.status) */}
              <div
                style={{
                  padding: '7px 10px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(255, 255, 255, 0.03)',
                  border: '1px solid rgba(255, 255, 255, 0.07)',
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                {managedVisuals.headerIcon}
                <div>
                  <div style={{ fontSize: 10, color: '#94a3b8' }}>{isZh ? '当前状态' : 'Status'}</div>
                  <div style={{ fontSize: 12, fontWeight: 600, color: managedVisuals.badgeColor }}>
                    {managedVisuals.badgeText}
                  </div>
                </div>
              </div>
            </div>

            {/* Dynamic Provider Status Notice Banner (Strictly rendering provider message with zero hardcoded error) */}
            <div
              className="managed-status-banner"
              style={{
                padding: '12px 14px',
                borderRadius: 8,
                backgroundColor: managedVisuals.bannerBg,
                border: `1px solid ${managedVisuals.bannerBorder}`,
                color: managedVisuals.bannerText,
                fontSize: 12,
                lineHeight: 1.5,
                marginBottom: 14,
              }}
            >
              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  fontWeight: 700,
                  color: managedVisuals.bannerHeaderColor,
                  marginBottom: 4,
                  fontSize: 13,
                }}
              >
                {managedVisuals.headerIcon}
                <span>{managedVisuals.headerTitle}</span>
                <span
                  style={{
                    fontSize: 11,
                    padding: '1px 6px',
                    borderRadius: 4,
                    backgroundColor: 'rgba(0, 0, 0, 0.25)',
                    fontFamily: 'monospace',
                  }}
                >
                  {managedStatus}
                </span>
              </div>
              <div style={{ color: managedVisuals.bannerText }}>
                {managedMessage}
              </div>
              {isLocalValidation && managedAccountInfo?.sessionId && (
                <div style={{ marginTop: 8, fontSize: 12 }}>
                  {isZh ? '托管账号：' : 'Worker: '}{managedAccountInfo.allocatedPlayerId}
                  {' · '}{isZh ? '房间：' : 'Room: '}{managedAccountInfo.currentRoomId}
                  {' · '}{isZh ? '租约已启用，保持此页面连接' : 'Lease active while this page stays connected'}
                </div>
              )}
            </div>
          </div>

          {/* Action Buttons */}
          <div style={{ display: 'flex', gap: 10, marginTop: 8, flexWrap: 'wrap' }}>
            {(!isManagedAvailable || !isLocalValidation) && (
              <button className="btn btn-primary" onClick={onStartLocalValidation}
                disabled={isLocalValidation && isManagedActive}
                style={{ padding: '9px 14px' }}>
                {isLocalValidation && isManagedActive
                  ? (isZh ? '本机托管验证已开启' : 'Local validation enabled')
                  : (isZh ? '开启本机托管验证' : 'Start local validation')}
              </button>
            )}
            {isManagedAvailable ? (
              <button
                className="btn btn-primary"
                onClick={onAllocateSession}
                style={{
                  flex: 1,
                  minWidth: 160,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 8,
                  padding: '9px 16px',
                  backgroundColor: '#16a34a',
                  borderColor: '#22c55e',
                  color: '#fff',
                  fontWeight: 600,
                }}
              >
                <Sparkles size={16} />
                <span>{isZh ? '申请托管观战会话' : 'Allocate Spectator Session'}</span>
              </button>
            ) : isManagedError ? (
              <button
                className="btn btn-secondary"
                onClick={() => onSelectMode('XINGSHU_MANAGED')}
                style={{
                  flex: 1,
                  minWidth: 160,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 8,
                  padding: '9px 16px',
                  borderColor: 'rgba(239, 68, 68, 0.4)',
                  color: '#f87171',
                  fontWeight: 600,
                }}
              >
                <RotateCcw size={15} />
                <span>{isZh ? '重试连接' : 'Retry'}</span>
              </button>
            ) : (
              <button
                className="btn btn-secondary"
                onClick={() => onSelectMode('XINGSHU_MANAGED')}
                style={{
                  flex: 1,
                  minWidth: 160,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 8,
                  padding: '9px 16px',
                  borderColor: 'rgba(245, 158, 11, 0.4)',
                  color: '#fbbf24',
                  fontWeight: 600,
                }}
              >
                <ExternalLink size={15} />
                <span>{isZh ? '查看官方账号状态' : 'Check Official Status'}</span>
              </button>
            )}

            {isManagedActive && (
              <button
                className="btn btn-primary"
                onClick={() => onSelectMode('USER_OWNED')}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  padding: '9px 14px',
                }}
              >
                <span>{isZh ? '切换至我的账号' : 'Switch to My Account'}</span>
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
