import React, { useEffect, useState, useRef } from 'react';
import { api } from '../../shared/api/tauri';
import type {
  GameInstallation,
  RuntimeVersion,
  InstalledRuntime,
  GameProcessStatus,
  BridgeConnectionState,
} from '../../shared/types';
import {
  deriveRuntimeTruthState,
  getVersionItemBadge,
  getBridgeStateDisplay,
  getGameProcessDisplay,
  CANONICAL_RUNTIME_HOST,
  CANONICAL_TRANSPORT,
} from './runtimeTruth';
import {
  Cpu,
  CheckCircle2,
  AlertCircle,
  RefreshCw,
  Zap,
  Shield,
  Layers,
  ArrowRightLeft,
  Radio,
  Activity,
  AlertTriangle,
  Server,
  FileCheck2,
} from 'lucide-react';
import { useTranslation } from '../../i18n';

export const RuntimePage: React.FC = () => {
  const { locale, t } = useTranslation();
  const isZh = locale === 'zh-CN';

  const [game, setGame] = useState<GameInstallation | null>(null);
  const [versions, setVersions] = useState<RuntimeVersion[]>([]);
  const [selectedVersionId, setSelectedVersionId] = useState<string>('');
  const [installedRuntime, setInstalledRuntime] = useState<InstalledRuntime | null>(null);
  const [gameProcess, setGameProcess] = useState<GameProcessStatus | null>(null);
  const [bridgeStatus, setBridgeStatus] = useState<BridgeConnectionState | null>(null);

  const [loading, setLoading] = useState(true);
  const [switching, setSwitching] = useState(false);
  const [channel, setChannel] = useState<'STEAM_CN' | 'STEAM_INT' | 'STANDALONE'>('STEAM_CN');

  const timerRef = useRef<number | null>(null);

  const fetchData = async () => {
    try {
      const [g, r, proc, b, inst] = await Promise.all([
        api.getPrimaryGame().catch(() => null),
        api.getRuntimeStatus(),
        api.checkGameProcessStatus(channel).catch(() => null),
        api.getBridgeStatus().catch(() => null),
        api.getInstalledRuntime().catch(() => null),
      ]);

      setGame(g);
      setVersions(r.versions || []);
      if (!selectedVersionId && r.active_version) {
        setSelectedVersionId(r.active_version);
      }
      setInstalledRuntime(r.installed_runtime || inst || null);
      setGameProcess(proc);
      setBridgeStatus(b);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
    timerRef.current = window.setInterval(fetchData, 2500);
    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
    };
  }, [channel]);

  const handleSwitchVersion = async (versionId: string) => {
    setSwitching(true);
    setSelectedVersionId(versionId);
    try {
      await api.switchRuntimeVersion(versionId);
      await fetchData();
    } finally {
      setSwitching(false);
    }
  };

  // Authoritative Truth Derivation (HS4-P1.3B Trichotomy)
  const truth = deriveRuntimeTruthState({
    selectedVersionId,
    versions,
    installedRuntime,
    gameProcess,
    bridgeStatus,
  });

  const bridgeDisplay = getBridgeStateDisplay(truth.bridge_state, isZh);
  const gameProcDisplay = getGameProcessDisplay(
    truth.active_session.game_running,
    truth.active_session.game_pid,
    isZh
  );

  return (
    <div>
      {/* Title Header */}
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
          <h1 style={{ fontSize: 22, fontWeight: 700, margin: 0 }}>{t('runtime.title')}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('runtime.subtitle')}
          </p>
        </div>

        <button
          className="btn btn-secondary btn-sm"
          onClick={() => {
            setLoading(true);
            fetchData();
          }}
          disabled={loading}
          style={{ display: 'flex', alignItems: 'center', gap: 6 }}
        >
          <RefreshCw size={14} className={loading ? 'spin' : ''} />
          <span>{t('runtime.redetect')}</span>
        </button>
      </div>

      {/* ======================================================== */}
      {/* SECTION 1: Trichotomy & Semantic Truth Header Summary    */}
      {/* Must display: 已选择方案, 已安装版本, 当前游戏实际状态, Bridge 状态 */}
      {/* ======================================================== */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))',
          gap: 14,
          marginBottom: 20,
        }}
      >
        {/* Card 1: 已选择方案 (Selected Plan) */}
        <div
          className="panel"
          style={{
            margin: 0,
            padding: 16,
            background: 'var(--bg-card)',
            borderRadius: 'var(--radius-md)',
            border: '1px solid var(--border-color)',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              {isZh ? '已选择方案' : 'Selected Plan'}
            </span>
            <Layers size={16} color="var(--primary)" />
          </div>
          <div
            style={{
              fontSize: 15,
              fontWeight: 700,
              marginTop: 8,
              color: 'var(--text-primary)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {truth.selected_plan?.version_name || selectedVersionId || (isZh ? '未指定' : 'Unspecified')}
          </div>
          <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 4 }}>
            {isZh ? '用户配置方案' : 'User Configured Plan'} | {truth.selected_plan?.runtime_type || 'APX'}
          </div>
        </div>

        {/* Card 2: 已安装版本 (Installed Version) */}
        <div
          className="panel"
          style={{
            margin: 0,
            padding: 16,
            background: 'var(--bg-card)',
            borderRadius: 'var(--radius-md)',
            border: '1px solid var(--border-color)',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              {isZh ? '已安装版本' : 'Installed Version'}
            </span>
            <FileCheck2 size={16} color="var(--accent)" />
          </div>
          <div
            style={{
              fontSize: 15,
              fontWeight: 700,
              marginTop: 8,
              color: 'var(--text-primary)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {truth.installed_runtime?.version_name || (isZh ? '物理就绪' : 'Ready on Disk')}
          </div>
          <div
            style={{
              fontSize: 11,
              fontFamily: 'var(--font-mono)',
              color: 'var(--text-secondary)',
              marginTop: 4,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {truth.installed_runtime?.install_path || 'com.unity.addressables/.../__data'}
          </div>
        </div>

        {/* Card 3: 当前游戏实际状态 (Actual Game State) */}
        <div
          className="panel"
          style={{
            margin: 0,
            padding: 16,
            background: 'var(--bg-card)',
            borderRadius: 'var(--radius-md)',
            border: '1px solid var(--border-color)',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              {isZh ? '当前游戏实际状态' : 'Actual Game State'}
            </span>
            <Activity
              size={16}
              color={truth.active_session.game_running ? 'var(--success)' : 'var(--text-muted)'}
            />
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 8 }}>
            <span className={gameProcDisplay.badgeClass}>
              {gameProcDisplay.text}
            </span>
            {truth.active_session.game_pid && (
              <span style={{ fontSize: 13, fontFamily: 'var(--font-mono)', color: 'var(--text-primary)' }}>
                PID: {truth.active_session.game_pid}
              </span>
            )}
          </div>
          <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 4 }}>
            {gameProcDisplay.detail}
          </div>
        </div>

        {/* Card 4: Bridge 状态 (Bridge State) */}
        <div
          className="panel"
          style={{
            margin: 0,
            padding: 16,
            background: 'var(--bg-card)',
            borderRadius: 'var(--radius-md)',
            border: '1px solid var(--border-color)',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              {isZh ? 'Bridge 状态' : 'Bridge State'}
            </span>
            <Radio
              size={16}
              color={truth.bridge_state === 'WAITING_CONNECTION' ? 'var(--warning)' : 'var(--success)'}
            />
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 8 }}>
            <span className={bridgeDisplay.badgeClass}>{bridgeDisplay.text}</span>
          </div>
          <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 4 }}>
            {truth.active_session.active_transport}
          </div>
        </div>
      </div>

      {/* ======================================================== */}
      {/* SECTION 2: Prominent "运行时会话真实状态" Truth Card      */}
      {/* ======================================================== */}
      <div
        className="panel"
        style={{
          marginBottom: 20,
          background:
            truth.status_category === 'LIVE_SESSION_ACTIVE'
              ? 'rgba(34, 197, 94, 0.05)'
              : truth.status_category === 'LEGACY_WARNING'
              ? 'rgba(239, 68, 68, 0.05)'
              : 'var(--bg-card)',
          border:
            truth.status_category === 'LIVE_SESSION_ACTIVE'
              ? '1px solid rgba(34, 197, 94, 0.3)'
              : truth.status_category === 'LEGACY_WARNING'
              ? '1px solid rgba(239, 68, 68, 0.3)'
              : '1px solid var(--border-color)',
        }}
      >
        <div className="panel-header" style={{ marginBottom: 16 }}>
          <div className="panel-title" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Server size={18} color="var(--primary)" />
            <span style={{ fontWeight: 700 }}>
              {isZh ? '运行时会话真实状态' : 'Runtime Session Truth'}
            </span>
          </div>

          <div>
            {truth.status_category === 'LIVE_SESSION_ACTIVE' && (
              <span className="badge badge-success">
                {isZh
                  ? `【当前会话活跃 (PID: ${truth.active_session.game_pid})】`
                  : `[Active Session (PID: ${truth.active_session.game_pid})]`}
              </span>
            )}
            {truth.status_category === 'WAITING_RUNTIME_CONNECTION' && (
              <span className="badge badge-warning">
                {isZh ? '【已安装 / 等待 Runtime 连接】' : '[Installed / Waiting Connection]'}
              </span>
            )}
            {truth.status_category === 'READY_NOT_RUNNING' && (
              <span className="badge badge-primary">
                {isZh ? '【已就绪 / 游戏未运行】' : '[Ready / Game Not Running]'}
              </span>
            )}
            {truth.status_category === 'LEGACY_WARNING' && (
              <span className="badge badge-danger">
                {isZh ? '【旧版 Runtime 未连接】' : '[Legacy Runtime Disconnected]'}
              </span>
            )}
          </div>
        </div>

        {/* Legacy Alert Banner if detected */}
        {truth.legacy_detected && (
          <div
            style={{
              padding: 14,
              marginBottom: 16,
              background: 'rgba(239, 68, 68, 0.1)',
              border: '1px solid rgba(239, 68, 68, 0.3)',
              borderRadius: 'var(--radius-sm)',
              display: 'flex',
              alignItems: 'flex-start',
              gap: 12,
            }}
          >
            <AlertTriangle size={18} color="var(--danger)" style={{ marginTop: 2, flexShrink: 0 }} />
            <div>
              <div style={{ fontWeight: 700, color: 'var(--danger)', fontSize: 13 }}>
                {isZh ? '检测到旧版 Runtime，当前 XingShu Bridge 未连接' : 'Legacy Runtime detected, XingShu Bridge disconnected'}
              </div>
              <p style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4, margin: 0 }}>
                {isZh
                  ? `游戏主进程已在运行 (PID: ${truth.active_session.game_pid || '--'})，但未检测到符合星枢规范的 HELLO 握手信号（当前可能运行旧版 RC1.1 或旧式管道）。请重新部署 APX 最新生产核心。`
                  : `Game process is active (PID: ${truth.active_session.game_pid || '--'}), but standard HELLO handshake was not received. Legacy RC1.1 runtime suspected.`}
              </p>
            </div>
          </div>
        )}

        {/* 4 Truth Metrics Grid */}
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
            gap: 12,
          }}
        >
          {/* Metric 1: 游戏进程 (Game Process) */}
          <div
            style={{
              background: 'var(--bg-secondary)',
              padding: '12px 14px',
              borderRadius: 'var(--radius-sm)',
              border: '1px solid var(--border-color)',
            }}
          >
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
              {isZh ? '游戏进程 (Game Process)' : 'Game Process'}
            </div>
            <div style={{ fontSize: 13, fontWeight: 600, marginTop: 4, color: 'var(--text-primary)' }}>
              {truth.active_session.game_running
                ? `PID: ${truth.active_session.game_pid || '--'} (${isZh ? '运行中' : 'Running'})`
                : isZh
                ? '未运行'
                : 'Not Running'}
            </div>
            <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 2 }}>
              {game?.executable_path ? 'AstralParty_CN.exe' : 'AstralParty.exe'}
            </div>
          </div>

          {/* Metric 2: 规范宿主 (Canonical Host) */}
          <div
            style={{
              background: 'var(--bg-secondary)',
              padding: '12px 14px',
              borderRadius: 'var(--radius-sm)',
              border: '1px solid var(--border-color)',
            }}
          >
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
              {isZh ? '规范宿主 (Canonical Host)' : 'Canonical Host'}
            </div>
            <div
              style={{
                fontSize: 13,
                fontWeight: 600,
                marginTop: 4,
                fontFamily: 'var(--font-mono)',
                color: 'var(--primary)',
              }}
            >
              {CANONICAL_RUNTIME_HOST}
            </div>
            <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 2 }}>
              {isZh ? '标准注入托管宿主' : 'Standard Injection Host'}
            </div>
          </div>

          {/* Metric 3: 活跃传输 (Active Transport) */}
          <div
            style={{
              background: 'var(--bg-secondary)',
              padding: '12px 14px',
              borderRadius: 'var(--radius-sm)',
              border: '1px solid var(--border-color)',
            }}
          >
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
              {isZh ? '活跃传输 (Active Transport)' : 'Active Transport'}
            </div>
            <div
              style={{
                fontSize: 13,
                fontWeight: 600,
                marginTop: 4,
                fontFamily: 'var(--font-mono)',
                color: 'var(--accent)',
              }}
            >
              {CANONICAL_TRANSPORT}
            </div>
            <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 2 }}>
              {isZh ? '高可靠 TCP 环回桥接' : 'Reliable Loopback TCP'}
            </div>
          </div>

          {/* Metric 4: 握手状态 (Handshake) */}
          <div
            style={{
              background: 'var(--bg-secondary)',
              padding: '12px 14px',
              borderRadius: 'var(--radius-sm)',
              border: '1px solid var(--border-color)',
            }}
          >
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
              {isZh ? '握手状态 (Handshake)' : 'Handshake'}
            </div>
            <div
              style={{
                fontSize: 13,
                fontWeight: 600,
                marginTop: 4,
                fontFamily: 'var(--font-mono)',
                color:
                  truth.active_session.handshake_status === 'HELLO_ACK'
                    ? 'var(--success)'
                    : truth.active_session.handshake_status === 'LEGACY_DETECTED'
                    ? 'var(--danger)'
                    : 'var(--warning)',
              }}
            >
              {truth.active_session.handshake_status}
            </div>
            <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 2 }}>
              {truth.active_session.handshake_status === 'HELLO_ACK'
                ? isZh
                  ? '协议双向确认'
                  : 'Duplex Confirmed'
                : truth.active_session.handshake_status === 'HELLO_PENDING'
                ? isZh
                  ? '等待 Runtime HELLO'
                  : 'Awaiting HELLO'
                : isZh
                  ? '等待游戏启动'
                  : 'Awaiting Launch'}
            </div>
          </div>
        </div>
      </div>

      {/* Target Game Environment */}
      <div className="panel" style={{ marginBottom: 20 }}>
        <div className="panel-header">
          <div className="panel-title">
            <Cpu size={18} color="var(--primary)" />
            <span>{t('runtime.targetEnv')}</span>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 16 }}>
          <div
            style={{
              background: 'var(--bg-card)',
              padding: 16,
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-color)',
            }}
          >
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginBottom: 6 }}>
              {t('runtime.channel')}
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              {(['STEAM_CN', 'STEAM_INT', 'STANDALONE'] as const).map((ch) => (
                <button
                  key={ch}
                  className={`btn btn-sm ${channel === ch ? 'btn-primary' : 'btn-secondary'}`}
                  onClick={() => setChannel(ch)}
                >
                  {ch === 'STEAM_CN'
                    ? t('runtime.steamCn')
                    : ch === 'STEAM_INT'
                    ? t('runtime.steamInt')
                    : t('runtime.standalone')}
                </button>
              ))}
            </div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 8 }}>
              AppID: <span className="badge badge-default">2622000</span> |{' '}
              {isZh ? '协议架构' : 'Architecture'}: x64 Unity Mono / IL2CPP
            </div>
          </div>

          <div
            style={{
              background: 'var(--bg-card)',
              padding: 16,
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-color)',
            }}
          >
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginBottom: 6 }}>
              {t('runtime.gameRootCheck')}
            </div>
            <div
              style={{
                fontSize: 13,
                fontFamily: 'var(--font-mono)',
                wordBreak: 'break-all',
                color: 'var(--text-primary)',
              }}
            >
              {game?.install_path || t('runtime.scanningPath')}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 8 }}>
              {game?.is_valid ? (
                <span className="badge badge-success">
                  <CheckCircle2 size={12} style={{ marginRight: 4 }} />
                  {t('runtime.integrityPass')}
                </span>
              ) : (
                <span className="badge badge-warning">
                  <AlertCircle size={12} style={{ marginRight: 4 }} />
                  {t('runtime.integrityPending')}
                </span>
              )}
              <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                {t('runtime.mainExecutable')}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* ======================================================== */}
      {/* SECTION 3: Runtime Versions Matrix with Accurate Badges   */}
      {/* ======================================================== */}
      <div className="panel">
        <div className="panel-header">
          <div className="panel-title">
            <Layers size={18} color="var(--accent)" />
            <span>{t('runtime.availableVersions')}</span>
          </div>
          <span className="badge badge-primary">
            {t('runtime.activeBadge')}: {truth.selected_plan?.version_name || selectedVersionId}
          </span>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {versions.map((ver) => {
            const badgeInfo = getVersionItemBadge(ver, truth, isZh);
            const isSelected =
              ver.version_id === truth.selected_plan?.version_id ||
              ver.version_name === truth.selected_plan?.version_name;

            return (
              <div
                key={ver.version_id}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  padding: 16,
                  background: badgeInfo.isLiveActive
                    ? 'rgba(34, 197, 94, 0.08)'
                    : isSelected
                    ? 'rgba(99, 102, 241, 0.08)'
                    : 'var(--bg-card)',
                  borderRadius: 'var(--radius-md)',
                  border: badgeInfo.isLiveActive
                    ? '1px solid var(--success)'
                    : isSelected
                    ? '1px solid var(--primary)'
                    : '1px solid var(--border-color)',
                  transition: 'all 0.2s ease',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
                  <div
                    style={{
                      width: 40,
                      height: 40,
                      borderRadius: 'var(--radius-sm)',
                      background:
                        ver.runtime_type === 'APX' ? 'var(--primary-glow)' : 'var(--bg-secondary)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      color: ver.runtime_type === 'APX' ? 'var(--primary)' : 'var(--text-secondary)',
                    }}
                  >
                    {ver.runtime_type === 'APX' ? <Zap size={20} /> : <Shield size={20} />}
                  </div>

                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                      <span style={{ fontSize: 14, fontWeight: 600 }}>{ver.version_name}</span>

                      {/* Truthful Badge */}
                      <span className={badgeInfo.badgeClass}>{badgeInfo.label}</span>

                      <span className="badge badge-default">
                        {isZh ? '类型' : 'Type'}: {ver.runtime_type}
                      </span>
                    </div>
                    <p style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4, margin: 0 }}>
                      {ver.description}
                    </p>
                  </div>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  {badgeInfo.isLiveActive ? (
                    <button className="btn btn-secondary btn-sm" disabled>
                      <CheckCircle2 size={14} color="var(--success)" />
                      <span>{isZh ? '当前会话活跃' : 'Live Session'}</span>
                    </button>
                  ) : isSelected ? (
                    <button className="btn btn-secondary btn-sm" disabled>
                      <CheckCircle2 size={14} color="var(--primary)" />
                      <span>{isZh ? '当前已选方案' : 'Selected Plan'}</span>
                    </button>
                  ) : (
                    <button
                      className="btn btn-primary btn-sm"
                      disabled={switching}
                      onClick={() => handleSwitchVersion(ver.version_id)}
                    >
                      <ArrowRightLeft size={14} />
                      <span>{t('runtime.switchTo')}</span>
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>

        <div
          style={{
            marginTop: 20,
            padding: 14,
            background: 'var(--bg-secondary)',
            borderRadius: 'var(--radius-sm)',
            border: '1px solid var(--border-color)',
            fontSize: 12,
            color: 'var(--text-secondary)',
            display: 'flex',
            alignItems: 'center',
            gap: 8,
          }}
        >
          <Shield size={16} color="var(--info)" />
          <span>
            <strong>{t('runtime.safetyNoteTitle')}:</strong> {t('runtime.safetyNoteDesc')}
          </span>
        </div>
      </div>
    </div>
  );
};
