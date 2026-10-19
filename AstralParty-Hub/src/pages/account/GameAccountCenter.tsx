import React, { useState, useEffect, useCallback } from 'react';
import {
  ShieldCheck,
  User,
  Eye,
  Activity,
  AlertTriangle,
  Trash2,
  Plus,
  RefreshCw,
  Laptop,
} from 'lucide-react';
import {
  gameAccountService,
  type GameAccountBinding,
  type CurrentRuntimeIdentity,
} from '../../services/gameAccountService';
import {
  GameAccountRole,
} from '../../types/gameAccount';
import type { RuntimeSession } from '../../types/runtimeSession';
import { api } from '../../shared/api/tauri';
import { useTranslation } from '../../i18n';

export const GameAccountCenter: React.FC = () => {
  const { locale } = useTranslation();
  const isZh = locale === 'zh-CN';

  const [bindings, setBindings] = useState<GameAccountBinding[]>(() =>
    gameAccountService.getBindings()
  );
  const [livePlayerId, setLivePlayerId] = useState<number | null>(null);
  const [spectatorPayloadPid, setSpectatorPayloadPid] = useState<number | null>(null);
  const [runtimeConnected, setRuntimeConnected] = useState<boolean>(false);
  const [runtimeSessions, setRuntimeSessions] = useState<RuntimeSession[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);

  // Manual Bind Modal / Form State
  const [showManualBind, setShowManualBind] = useState<boolean>(false);
  const [manualRole, setManualRole] = useState<GameAccountRole>('MAIN');
  const [manualPlayerId, setManualPlayerId] = useState<string>('');
  const [manualDisplayName, setManualDisplayName] = useState<string>('');
  const [manualPlatform, setManualPlatform] = useState<string>('STEAM_CN');
  const [formError, setFormError] = useState<string | null>(null);

  // Reload bindings when service updates
  useEffect(() => {
    const unsubscribe = gameAccountService.subscribe(() => {
      setBindings(gameAccountService.getBindings());
    });
    return unsubscribe;
  }, []);

  // Sync selectedSessionId if query parameter specifies target platform
  useEffect(() => {
    if (typeof window !== 'undefined' && (window.location.search.includes('session=taptap') || window.location.search.includes('session=spectator'))) {
      const tapSession = runtimeSessions.find((s) => s.platformId === 'TAPTAP');
      if (tapSession) {
        setSelectedSessionId(tapSession.connectionId);
      }
    }
  }, [runtimeSessions]);

  // Poll runtime session status
  const pollRuntimeSession = useCallback(async () => {
    try {
      const [payload, bridge, sessions] = await Promise.all([
        api.getSpectatorHandPayload().catch(() => null),
        api.getBridgeStatus().catch(() => null),
        api.getRuntimeSessions().catch(() => []),
      ]);

      const activeSessions = (sessions || []).filter((s: RuntimeSession) => s.status !== 'DISCONNECTED');
      setRuntimeSessions(activeSessions);

      const isConnected = !!(
        (bridge?.runtimeConnected &&
          ((bridge as any)?.activeTransport === 'LOOPBACK_TCP' || (bridge as any)?.tcpConnected)) ||
        activeSessions.length > 0
      );
      setRuntimeConnected(isConnected);

      if (isConnected && payload && typeof payload.spectatorAccountPlayerId === 'number' && payload.spectatorAccountPlayerId > 0) {
        setSpectatorPayloadPid(payload.spectatorAccountPlayerId);
        setLivePlayerId(payload.spectatorAccountPlayerId);
        gameAccountService.touchLastSeen(payload.spectatorAccountPlayerId);
      } else {
        setSpectatorPayloadPid(null);
        const identifiedSession = activeSessions.find((s: RuntimeSession) => typeof s.playerId === 'number' && (s.playerId ?? 0) > 0);
        if (identifiedSession && identifiedSession.playerId) {
          setLivePlayerId(identifiedSession.playerId);
          gameAccountService.touchLastSeen(identifiedSession.playerId);
        } else {
          setLivePlayerId(null);
        }
      }

      // Touch last seen for active sessions
      for (const s of activeSessions) {
        if (typeof s.playerId === 'number' && s.playerId > 0) {
          gameAccountService.touchLastSeen(s.playerId);
        }
      }
    } catch {
      setRuntimeConnected(false);
      setLivePlayerId(null);
      setSpectatorPayloadPid(null);
      setRuntimeSessions([]);
    }
  }, []);

  useEffect(() => {
    pollRuntimeSession();
    const interval = setInterval(pollRuntimeSession, 2000);
    return () => clearInterval(interval);
  }, [pollRuntimeSession]);

  const handleManualRefresh = async () => {
    setIsRefreshing(true);
    await pollRuntimeSession();
    setBindings(gameAccountService.getBindings());
    setTimeout(() => setIsRefreshing(false), 300);
  };

  const mainBinding = bindings.find((b) => b.role === 'MAIN') || null;
  const spectatorBinding = bindings.find((b) => b.role === 'USER_SPECTATOR') || null;
  const [spectatorExecutable, setSpectatorExecutable] = useState('');
  const [launchFeedback, setLaunchFeedback] = useState('');
  useEffect(() => {
    setSpectatorExecutable(spectatorBinding ? localStorage.getItem(`xingshu.launcher.${spectatorBinding.platformId}.executablePath`) ?? '' : '');
    setLaunchFeedback('');
  }, [spectatorBinding?.platformId]);

  // Multi-session separation & active session resolution
  const connectedCount = runtimeSessions.length > 0 ? runtimeSessions.length : (runtimeConnected ? 1 : 0);
  const anyRuntimeConnected = connectedCount > 0;
  const mainRuntimeSession = runtimeSessions.find((s) => s.role === 'MAIN' || s.platformId === 'STEAM_CN') || null;
  const spectatorRuntimeSession = runtimeSessions.find((s) => s.role === 'USER_SPECTATOR' || s.platformId === 'TAPTAP') || null;
  const selectedRuntimeSession =
    (selectedSessionId ? runtimeSessions.find((s) => s.connectionId === selectedSessionId) : null) ||
    mainRuntimeSession ||
    spectatorRuntimeSession ||
    runtimeSessions[0] ||
    null;

  const currentPlatform = selectedRuntimeSession?.platformId || (selectedRuntimeSession === spectatorRuntimeSession ? 'TAPTAP' : 'STEAM_CN');

  // STRICT IDENTITY RULE:
  // Local Runtime identity may ONLY come from:
  // A. explicit authenticated local player identity (session.playerId)
  // B. spectatorAccountPlayerId (spectatorPayloadPid)
  // NEVER from snapshot.playerId / payload.playerId (which is the watched remote player whose hand payload is being emitted).
  // Steam CN sessions NEVER borrow spectatorPayloadPid from TapTap.
  const effectiveLivePlayerId = (() => {
    if (selectedRuntimeSession) {
      if (typeof selectedRuntimeSession.playerId === 'number' && selectedRuntimeSession.playerId > 0) {
        return selectedRuntimeSession.playerId;
      }
      if (selectedRuntimeSession.platformId === 'TAPTAP') {
        return spectatorPayloadPid;
      }
      return null;
    }
    return spectatorPayloadPid ?? livePlayerId;
  })();

  const isSelectedConnected = anyRuntimeConnected && (!!selectedRuntimeSession || runtimeConnected);

  // Resolve active runtime identity
  const currentIdentity: CurrentRuntimeIdentity = gameAccountService.resolveSession(
    isSelectedConnected ? effectiveLivePlayerId : null,
    currentPlatform,
    isSelectedConnected
  );

  const handleUnbind = (bindingId: string) => {
    gameAccountService.unbindAccount(bindingId);
  };

  const handleBindCurrentSession = (role: GameAccountRole) => {
    if (!effectiveLivePlayerId) return;
    const defaultName = role === 'MAIN' ? '我的主账号' : '我的专用观战账号';
    const targetPlatform = role === 'USER_SPECTATOR' ? 'TAPTAP' : (currentPlatform || 'STEAM_CN');
    gameAccountService.bindAccount(effectiveLivePlayerId, role, targetPlatform, defaultName);
  };

  const handleOpenManualBind = (role: GameAccountRole) => {
    setManualRole(role);
    setManualPlayerId(
      effectiveLivePlayerId
        ? String(effectiveLivePlayerId)
        : ''
    );
    setManualDisplayName(role === 'MAIN' ? '主账号' : '专用观战账号');
    setManualPlatform(role === 'USER_SPECTATOR' ? 'TAPTAP' : (currentPlatform || 'STEAM_CN'));
    setFormError(null);
    setShowManualBind(true);
  };

  const handleSubmitManualBind = (e: React.FormEvent) => {
    e.preventDefault();
    const pid = parseInt(manualPlayerId.trim(), 10);
    if (isNaN(pid) || pid <= 0) {
      setFormError(isZh ? '请输入有效的正整数 Player ID' : 'Please enter a valid positive Player ID');
      return;
    }

    try {
      gameAccountService.bindAccount(
        pid,
        manualRole,
        manualPlatform,
        manualDisplayName.trim() || null
      );
      setShowManualBind(false);
      setFormError(null);
    } catch (err: any) {
      setFormError(err.message || '绑定失败');
    }
  };

  const formatTimestamp = (iso: string | null) => {
    if (!iso) return isZh ? '暂无记录' : 'N/A';
    try {
      const d = new Date(iso);
      return d.toLocaleString(isZh ? 'zh-CN' : 'en-US', {
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
      });
    } catch {
      return iso;
    }
  };

  return (
    <div style={{ maxWidth: 1200, margin: '0 auto', paddingBottom: 40 }}>
      {spectatorBinding && <div className="panel" style={{ padding: 16, marginBottom: 18 }}>
        <strong>观战客户端启动设置 · {spectatorBinding.platformId}</strong>
        <p style={{ color: '#94a3b8', fontSize: 13 }}>自动观战使用所绑定的平台。Steam 通过 Steam 启动；其他平台可填写该平台游戏 .exe 的完整路径。</p>
        <div style={{ display: 'flex', gap: 8 }}>
          <input aria-label="观战客户端程序路径" value={spectatorExecutable} onChange={e => setSpectatorExecutable(e.target.value)} placeholder="留空使用已检测的平台路径" style={{ flex: 1 }} />
          <button onClick={() => {
            localStorage.setItem(`xingshu.launcher.${spectatorBinding.platformId}.executablePath`, spectatorExecutable.trim());
            setLaunchFeedback('已保存此平台的启动路径');
          }}>保存路径</button>
          <button onClick={async () => {
            localStorage.setItem(`xingshu.launcher.${spectatorBinding.platformId}.executablePath`, spectatorExecutable.trim());
            const result = await api.launchGameUri(undefined, spectatorBinding.platformId);
            setLaunchFeedback(result.message);
          }}>启动观战端</button>
        </div>
        {launchFeedback && <p role="status">{launchFeedback}</p>}
      </div>}
      {/* 1. Header */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: 20,
          flexWrap: 'wrap',
          gap: 12,
        }}
      >
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <h1 style={{ fontSize: 22, fontWeight: 700, margin: 0, color: '#fff' }}>
              {isZh ? '游戏账号中心' : 'Game Account Center'}
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
              HS4-P1.4
            </span>
          </div>
          <p style={{ color: 'var(--text-secondary, #94a3b8)', fontSize: 13, marginTop: 4, marginBottom: 0 }}>
            {isZh
              ? '主账号与观战账号独立显式绑定体系，保护主力对局安全'
              : 'Explicit identity separation and role bindings for primary and spectator accounts'}
          </p>
        </div>

        <button
          className="btn btn-secondary"
          onClick={handleManualRefresh}
          disabled={isRefreshing}
          style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '7px 14px' }}
        >
          <RefreshCw size={14} className={isRefreshing ? 'spin' : ''} />
          <span>{isZh ? '刷新状态' : 'Refresh'}</span>
        </button>
      </div>

      {/* 2. Zero-Credential Privacy Guarantee Banner */}
      <div
        style={{
          padding: '12px 18px',
          marginBottom: 24,
          borderRadius: 8,
          backgroundColor: 'rgba(16, 185, 129, 0.08)',
          border: '1px solid rgba(16, 185, 129, 0.25)',
          display: 'flex',
          alignItems: 'flex-start',
          gap: 12,
        }}
      >
        <ShieldCheck size={20} color="#10b981" style={{ flexShrink: 0, marginTop: 2 }} />
        <div style={{ fontSize: 13, color: '#cbd5e1', lineHeight: 1.5 }}>
          <strong style={{ color: '#10b981' }}>
            {isZh ? '零凭据与隐私安全承诺' : 'Zero-Credential & Privacy Guarantee'}
          </strong>
          <div style={{ marginTop: 2, color: 'var(--text-secondary, #94a3b8)' }}>
            {isZh
              ? '星枢仅记录公开的游戏内 Player ID 与角色绑定关系，绝不保存、读取或上传任何 Steam 密码、令牌、2FA 秘钥或会话 Cookie。身份角色绝不自动猜测，必须由用户自主显式绑定。'
              : 'XingShu only stores public in-game Player IDs and explicit role bindings. Passwords, Steam credentials, tokens, or cookies are NEVER stored or transmitted.'}
          </div>
        </div>
      </div>

      {/* 3. Section 3: 【当前会话身份】(CURRENT_SESSION) */}
      <div
        className="panel"
        style={{
          marginBottom: 24,
          padding: '18px 20px',
          backgroundColor: 'rgba(26, 32, 44, 0.65)',
          borderRadius: 10,
          border: '1px solid rgba(255, 255, 255, 0.08)',
        }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Activity size={18} color="#38bdf8" />
            <h2 style={{ fontSize: 16, fontWeight: 600, color: '#fff', margin: 0 }}>
              {isZh ? '【当前会话身份】(CURRENT_SESSION)' : '【Current Session Identity】'}
            </h2>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <span
              style={{
                width: 8,
                height: 8,
                borderRadius: '50%',
                backgroundColor: connectedCount === 0 ? '#64748b' : connectedCount === 1 ? '#22c55e' : '#38bdf8',
                display: 'inline-block',
              }}
            />
            <span
              style={{
                fontSize: 12,
                color: connectedCount === 0 ? '#94a3b8' : connectedCount === 1 ? '#22c55e' : '#38bdf8',
                fontWeight: 600,
              }}
            >
              {connectedCount === 0
                ? (isZh ? '0 Runtime 已连接' : '0 Runtime Connected')
                : connectedCount === 1
                  ? (isZh ? '1 Runtime 已连接' : '1 Runtime Connected')
                  : (isZh ? '2 Runtime（双端在线）' : '2 Runtimes (Dual-Client Online)')}
            </span>
          </div>
        </div>

        {/* Multi-Session Switcher if both clients active */}
        {runtimeSessions.length > 1 && (
          <div style={{ display: 'flex', gap: 8, marginBottom: 14 }}>
            {runtimeSessions.map((s) => {
              const isSelected = (selectedRuntimeSession?.connectionId === s.connectionId) ||
                (!selectedSessionId && s.connectionId === runtimeSessions[0].connectionId);
              const isSteam = s.platformId === 'STEAM_CN';
              return (
                <button
                  key={s.connectionId}
                  onClick={() => setSelectedSessionId(s.connectionId)}
                  style={{
                    padding: '6px 12px',
                    borderRadius: 6,
                    border: isSelected ? '1px solid #38bdf8' : '1px solid rgba(255, 255, 255, 0.1)',
                    backgroundColor: isSelected ? 'rgba(56, 189, 248, 0.2)' : 'rgba(15, 23, 42, 0.4)',
                    color: isSelected ? '#38bdf8' : '#94a3b8',
                    cursor: 'pointer',
                    fontSize: 12,
                    fontWeight: 600,
                    display: 'flex',
                    alignItems: 'center',
                    gap: 6,
                  }}
                >
                  <Laptop size={14} />
                  <span>{isSteam ? 'Steam CN' : (s.platformId === 'TAPTAP' ? 'TapTap' : s.platformId)} (PID: {s.processId ?? s.gamePid ?? '?'})</span>
                </button>
              );
            })}
          </div>
        )}

        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
            gap: 16,
            backgroundColor: 'rgba(15, 23, 42, 0.5)',
            padding: 16,
            borderRadius: 8,
            border: '1px solid rgba(255, 255, 255, 0.05)',
          }}
        >
          <div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)' }}>
              {isZh ? '实时识别 Player ID' : 'Live Player ID'}
            </div>
            <div style={{ fontSize: 18, fontWeight: 700, color: '#fff', marginTop: 4, fontFamily: 'monospace' }}>
              {currentIdentity.currentSessionPlayerId ? currentIdentity.currentSessionPlayerId : (isZh ? '未识别 / 未进入对局' : 'None / Not in Room')}
            </div>
          </div>

          <div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)' }}>
              {isZh ? '判定解析角色' : 'Resolved Role'}
            </div>
            <div style={{ marginTop: 4 }}>
              {currentIdentity.resolvedRole === 'MAIN' ? (
                <span
                  style={{
                    display: 'inline-block',
                    padding: '3px 10px',
                    borderRadius: 4,
                    backgroundColor: 'rgba(34, 197, 94, 0.2)',
                    color: '#4ade80',
                    fontSize: 13,
                    fontWeight: 600,
                    border: '1px solid rgba(34, 197, 94, 0.4)',
                  }}
                >
                  ● {isZh ? '主账号 (MAIN)' : 'MAIN Account'}
                </span>
              ) : currentIdentity.resolvedRole === 'USER_SPECTATOR' ? (
                <span
                  style={{
                    display: 'inline-block',
                    padding: '3px 10px',
                    borderRadius: 4,
                    backgroundColor: 'rgba(99, 102, 241, 0.2)',
                    color: '#818cf8',
                    fontSize: 13,
                    fontWeight: 600,
                    border: '1px solid rgba(99, 102, 241, 0.4)',
                  }}
                >
                  ● {isZh ? '我的观战账号 (USER_SPECTATOR)' : 'User Spectator'}
                </span>
              ) : currentIdentity.resolvedRole === 'UNBOUND_ACCOUNT' ? (
                <span
                  style={{
                    display: 'inline-block',
                    padding: '3px 10px',
                    borderRadius: 4,
                    backgroundColor: 'rgba(234, 179, 8, 0.2)',
                    color: '#facc15',
                    fontSize: 13,
                    fontWeight: 600,
                    border: '1px solid rgba(234, 179, 8, 0.4)',
                  }}
                >
                  ▲ {isZh ? '未绑定账号 (UNBOUND_ACCOUNT)' : 'Unbound Account'}
                </span>
              ) : currentIdentity.resolvedRole === 'CONNECTED_PENDING_IDENTITY' ? (
                <span
                  style={{
                    display: 'inline-block',
                    padding: '3px 10px',
                    borderRadius: 4,
                    backgroundColor: 'rgba(56, 189, 248, 0.2)',
                    color: '#38bdf8',
                    fontSize: 13,
                    fontWeight: 600,
                    border: '1px solid rgba(56, 189, 248, 0.4)',
                  }}
                >
                  ● {isZh ? '已连接 Runtime（等待账号识别）' : 'Connected (Pending Identity)'}
                </span>
              ) : (
                <span
                  style={{
                    display: 'inline-block',
                    padding: '3px 10px',
                    borderRadius: 4,
                    backgroundColor: 'rgba(100, 116, 139, 0.2)',
                    color: '#94a3b8',
                    fontSize: 13,
                    fontWeight: 500,
                  }}
                >
                  ○ {isZh ? '未连接 Runtime (DISCONNECTED)' : 'Disconnected'}
                </span>
              )}
            </div>
          </div>

          <div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)' }}>
              {isZh ? '目标平台与进程' : 'Platform & Process'}
            </div>
            <div style={{ fontSize: 14, fontWeight: 500, color: '#e2e8f0', marginTop: 6 }}>
              {isSelectedConnected
                ? `${currentPlatform} (PID: ${selectedRuntimeSession?.processId ?? selectedRuntimeSession?.gamePid ?? '—'})`
                : (currentIdentity.platformId || 'STEAM_CN')}
            </div>
          </div>
        </div>

        {/* If current session is UNBOUND, show quick bind action buttons */}
        {currentIdentity.resolvedRole === 'UNBOUND_ACCOUNT' && currentIdentity.currentSessionPlayerId && (
          <div
            style={{
              marginTop: 14,
              padding: '12px 16px',
              borderRadius: 6,
              backgroundColor: 'rgba(234, 179, 8, 0.08)',
              border: '1px solid rgba(234, 179, 8, 0.25)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
              gap: 12,
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, color: '#fef08a' }}>
              <AlertTriangle size={16} color="#eab308" />
              <span>
                {isZh
                  ? `当前会话登录的账号 (PID: ${currentIdentity.currentSessionPlayerId}) 尚未绑定到任何角色。`
                  : `Current session account (PID: ${currentIdentity.currentSessionPlayerId}) is not bound.`}
              </span>
            </div>
            <div style={{ display: 'flex', gap: 10 }}>
              <button
                className="btn btn-primary"
                onClick={() => handleBindCurrentSession('MAIN')}
                style={{ fontSize: 12, padding: '5px 12px' }}
              >
                {isZh ? '设为主账号' : 'Set as MAIN'}
              </button>
              <button
                className="btn btn-secondary"
                onClick={() => handleBindCurrentSession('USER_SPECTATOR')}
                style={{ fontSize: 12, padding: '5px 12px' }}
              >
                {isZh ? '设为我的观战账号' : 'Set as Spectator'}
              </button>
            </div>
          </div>
        )}
      </div>

      {/* 4. Section 1 & Section 2: Account Role Cards */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))', gap: 20 }}>
        {/* Section 1: 【主账号】(MAIN) */}
        <div
          className="panel"
          style={{
            padding: 20,
            borderRadius: 10,
            backgroundColor: 'rgba(26, 32, 44, 0.65)',
            border: mainBinding
              ? '1px solid rgba(34, 197, 94, 0.35)'
              : '1px solid rgba(255, 255, 255, 0.08)',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
          }}
        >
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <User size={18} color="#4ade80" />
                <h3 style={{ fontSize: 16, fontWeight: 700, margin: 0, color: '#fff' }}>
                  {isZh ? '【主账号】(MAIN)' : '【Main Account】(MAIN)'}
                </h3>
              </div>
              <span
                style={{
                  fontSize: 11,
                  padding: '2px 8px',
                  borderRadius: 4,
                  backgroundColor: mainBinding ? 'rgba(34, 197, 94, 0.15)' : 'rgba(100, 116, 139, 0.15)',
                  color: mainBinding ? '#4ade80' : '#94a3b8',
                  fontWeight: 600,
                }}
              >
                {mainBinding ? (isZh ? '已绑定' : 'Bound') : (isZh ? '未绑定' : 'Unbound')}
              </span>
            </div>

            <p style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)', marginBottom: 16 }}>
              {isZh
                ? '用于日常游玩、段位竞技和排位匹配的核心账号。全知观战系统严禁使用主账号直接进入观战以免占用对局。'
                : 'Primary game account for matchmaking and rank. Protected against accidental spectator session hijacking.'}
            </p>

            {mainBinding ? (
              <div
                style={{
                  backgroundColor: 'rgba(15, 23, 42, 0.6)',
                  borderRadius: 8,
                  padding: 14,
                  border: '1px solid rgba(255, 255, 255, 0.05)',
                  marginBottom: 16,
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 10,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>Player ID</span>
                  <span style={{ fontSize: 14, fontWeight: 700, color: '#fff', fontFamily: 'monospace' }}>
                    {mainBinding.playerId}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>{isZh ? '账号昵称' : 'Display Name'}</span>
                  <span style={{ fontSize: 13, color: '#e2e8f0' }}>
                    {mainBinding.displayName || (isZh ? '未设置' : 'Not set')}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>{isZh ? '平台' : 'Platform'}</span>
                  <span style={{ fontSize: 13, color: '#38bdf8' }}>{mainBinding.platformId}</span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>{isZh ? '绑定时间' : 'Bound At'}</span>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>{formatTimestamp(mainBinding.boundAt)}</span>
                </div>
                {mainBinding.lastSeenAt && (
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ fontSize: 12, color: '#94a3b8' }}>{isZh ? '最后在线' : 'Last Seen'}</span>
                    <span style={{ fontSize: 12, color: '#94a3b8' }}>{formatTimestamp(mainBinding.lastSeenAt)}</span>
                  </div>
                )}
              </div>
            ) : (
              <div
                style={{
                  padding: '24px 16px',
                  borderRadius: 8,
                  backgroundColor: 'rgba(15, 23, 42, 0.3)',
                  border: '1px dashed rgba(255, 255, 255, 0.1)',
                  textAlign: 'center',
                  marginBottom: 16,
                }}
              >
                <User size={32} color="#64748b" style={{ margin: '0 auto 8px auto' }} />
                <div style={{ fontSize: 13, color: '#94a3b8' }}>
                  {isZh ? '当前尚未绑定主账号' : 'No main account bound yet'}
                </div>
              </div>
            )}
          </div>

          <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
            {mainBinding ? (
              <button
                className="btn btn-secondary"
                onClick={() => handleUnbind(mainBinding.bindingId)}
                style={{
                  flex: 1,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 6,
                  color: '#f87171',
                  borderColor: 'rgba(239, 68, 68, 0.3)',
                }}
              >
                <Trash2 size={14} />
                <span>{isZh ? '解除绑定' : 'Unbind'}</span>
              </button>
            ) : (
              <button
                className="btn btn-primary"
                onClick={() => handleOpenManualBind('MAIN')}
                style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6 }}
              >
                <Plus size={14} />
                <span>{isZh ? '设为主账号' : 'Set as MAIN'}</span>
              </button>
            )}
          </div>
        </div>

        {/* Section 2: 【我的观战账号】(USER_SPECTATOR) */}
        <div
          className="panel"
          style={{
            padding: 20,
            borderRadius: 10,
            backgroundColor: 'rgba(26, 32, 44, 0.65)',
            border: spectatorBinding
              ? '1px solid rgba(99, 102, 241, 0.35)'
              : '1px solid rgba(255, 255, 255, 0.08)',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
          }}
        >
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <Eye size={18} color="#818cf8" />
                <h3 style={{ fontSize: 16, fontWeight: 700, margin: 0, color: '#fff' }}>
                  {isZh ? '【我的观战账号】(USER_SPECTATOR)' : '【My Spectator Account】'}
                </h3>
              </div>
              <span
                style={{
                  fontSize: 11,
                  padding: '2px 8px',
                  borderRadius: 4,
                  backgroundColor: spectatorBinding ? 'rgba(99, 102, 241, 0.15)' : 'rgba(100, 116, 139, 0.15)',
                  color: spectatorBinding ? '#a5b4fc' : '#94a3b8',
                  fontWeight: 600,
                }}
              >
                {spectatorBinding ? (isZh ? '已绑定' : 'Bound') : (isZh ? '未绑定' : 'Unbound')}
              </span>
            </div>

            <p style={{ fontSize: 12, color: 'var(--text-secondary, #94a3b8)', marginBottom: 16 }}>
              {isZh
                ? '用户个人持有的专用小号/副机账号，专用于进入官方房间进行手牌透视与战局转播，与主账号彻底解耦。'
                : 'Dedicated user-owned secondary account solely used for room spectating and live hand streaming.'}
            </p>

            {spectatorBinding ? (
              <div
                style={{
                  backgroundColor: 'rgba(15, 23, 42, 0.6)',
                  borderRadius: 8,
                  padding: 14,
                  border: '1px solid rgba(255, 255, 255, 0.05)',
                  marginBottom: 16,
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 10,
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>Player ID</span>
                  <span style={{ fontSize: 14, fontWeight: 700, color: '#fff', fontFamily: 'monospace' }}>
                    {spectatorBinding.playerId}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>{isZh ? '账号昵称' : 'Display Name'}</span>
                  <span style={{ fontSize: 13, color: '#e2e8f0' }}>
                    {spectatorBinding.displayName || (isZh ? '未设置' : 'Not set')}
                  </span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>{isZh ? '平台' : 'Platform'}</span>
                  <span style={{ fontSize: 13, color: '#38bdf8' }}>{spectatorBinding.platformId}</span>
                </div>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>{isZh ? '绑定时间' : 'Bound At'}</span>
                  <span style={{ fontSize: 12, color: '#94a3b8' }}>{formatTimestamp(spectatorBinding.boundAt)}</span>
                </div>
                {spectatorBinding.lastSeenAt && (
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ fontSize: 12, color: '#94a3b8' }}>{isZh ? '最后在线' : 'Last Seen'}</span>
                    <span style={{ fontSize: 12, color: '#94a3b8' }}>{formatTimestamp(spectatorBinding.lastSeenAt)}</span>
                  </div>
                )}
              </div>
            ) : (
              <div
                style={{
                  padding: '24px 16px',
                  borderRadius: 8,
                  backgroundColor: 'rgba(15, 23, 42, 0.3)',
                  border: '1px dashed rgba(255, 255, 255, 0.1)',
                  textAlign: 'center',
                  marginBottom: 16,
                }}
              >
                <Eye size={32} color="#64748b" style={{ margin: '0 auto 8px auto' }} />
                <div style={{ fontSize: 13, color: '#94a3b8' }}>
                  {isZh ? '当前尚未绑定专用观战账号' : 'No spectator account bound yet'}
                </div>
              </div>
            )}
          </div>

          <div style={{ display: 'flex', gap: 10, marginTop: 8 }}>
            {spectatorBinding ? (
              <button
                className="btn btn-secondary"
                onClick={() => handleUnbind(spectatorBinding.bindingId)}
                style={{
                  flex: 1,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 6,
                  color: '#f87171',
                  borderColor: 'rgba(239, 68, 68, 0.3)',
                }}
              >
                <Trash2 size={14} />
                <span>{isZh ? '解除绑定' : 'Unbind'}</span>
              </button>
            ) : (
              <button
                className="btn btn-primary"
                onClick={() => handleOpenManualBind('USER_SPECTATOR')}
                style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6 }}
              >
                <Plus size={14} />
                <span>{isZh ? '设为观战账号' : 'Set as Spectator'}</span>
              </button>
            )}
          </div>
        </div>
      </div>

      {/* 5. Manual Bind Dialog Modal */}
      {showManualBind && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            backgroundColor: 'rgba(0, 0, 0, 0.65)',
            backdropFilter: 'blur(4px)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1000,
            padding: 20,
          }}
        >
          <div
            className="panel"
            style={{
              width: '100%',
              maxWidth: 480,
              backgroundColor: '#1e293b',
              borderRadius: 12,
              padding: 24,
              border: '1px solid rgba(255, 255, 255, 0.12)',
              boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.5)',
            }}
          >
            <h3 style={{ fontSize: 18, fontWeight: 700, margin: '0 0 16px 0', color: '#fff' }}>
              {manualRole === 'MAIN'
                ? (isZh ? '绑定主账号 (MAIN)' : 'Bind Main Account')
                : (isZh ? '绑定专用观战账号 (USER_SPECTATOR)' : 'Bind Spectator Account')}
            </h3>

            {formError && (
              <div
                style={{
                  padding: '8px 12px',
                  borderRadius: 6,
                  backgroundColor: 'rgba(239, 68, 68, 0.15)',
                  border: '1px solid rgba(239, 68, 68, 0.4)',
                  color: '#fca5a5',
                  fontSize: 12,
                  marginBottom: 14,
                }}
              >
                {formError}
              </div>
            )}

            <form onSubmit={handleSubmitManualBind}>
              <div style={{ marginBottom: 14 }}>
                <label style={{ display: 'block', fontSize: 12, color: '#94a3b8', marginBottom: 6 }}>
                  {isZh ? '游戏内 Player ID (正整数)' : 'In-game Player ID'}
                </label>
                <input
                  type="number"
                  className="input"
                  style={{ width: '100%', padding: '8px 12px', borderRadius: 6, backgroundColor: '#0f172a', border: '1px solid #334155', color: '#fff' }}
                  value={manualPlayerId}
                  onChange={(e) => setManualPlayerId(e.target.value)}
                  placeholder={isZh ? "例如: 1234567" : "e.g. 1234567"}
                  required
                />
              </div>

              <div style={{ marginBottom: 14 }}>
                <label style={{ display: 'block', fontSize: 12, color: '#94a3b8', marginBottom: 6 }}>
                  {isZh ? '自定义昵称 (可选)' : 'Display Name (Optional)'}
                </label>
                <input
                  type="text"
                  className="input"
                  style={{ width: '100%', padding: '8px 12px', borderRadius: 6, backgroundColor: '#0f172a', border: '1px solid #334155', color: '#fff' }}
                  value={manualDisplayName}
                  onChange={(e) => setManualDisplayName(e.target.value)}
                  placeholder="例如: 星空派对大师"
                />
              </div>

              <div style={{ marginBottom: 20 }}>
                <label style={{ display: 'block', fontSize: 12, color: '#94a3b8', marginBottom: 6 }}>
                  {isZh ? '游戏平台' : 'Platform'}
                </label>
                <select
                  className="input"
                  style={{ width: '100%', padding: '8px 12px', borderRadius: 6, backgroundColor: '#0f172a', border: '1px solid #334155', color: '#fff' }}
                  value={manualPlatform}
                  onChange={(e) => setManualPlatform(e.target.value)}
                >
                  <option value="STEAM_CN">Steam 国服 (STEAM_CN)</option>
                  <option value="STEAM_GLOBAL">Steam 国际服 (STEAM_GLOBAL)</option>
                  <option value="TAPTAP">TapTap 客户端 (TAPTAP)</option>
                  <option value="BILIBILI">Bilibili 客户端 (BILIBILI)</option>
                  <option value="WEGAME">WeGame 客户端 (WEGAME)</option>
                </select>
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10 }}>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setShowManualBind(false)}
                  style={{ padding: '7px 16px' }}
                >
                  {isZh ? '取消' : 'Cancel'}
                </button>
                <button
                  type="submit"
                  className="btn btn-primary"
                  style={{ padding: '7px 20px' }}
                >
                  {isZh ? '确认绑定' : 'Confirm Binding'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
export default GameAccountCenter;
