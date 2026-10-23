import React, { useEffect, useState, useCallback } from 'react';
import { listen } from '@tauri-apps/api/event';
import { api } from '../../shared/api/tauri';
import type { DiagnosticReport, SpectatorHandPayload, BridgeConnectionState } from '../../shared/types';
import { Activity, RefreshCw, CheckCircle2, AlertTriangle, AlertCircle, Info, Cpu, HardDrive, Eye, Radio } from 'lucide-react';
import { useTranslation } from '../../i18n';
import { useSearchParams } from 'react-router-dom';
import { RuntimeDiagnosticsPanel } from '../runtime/RuntimePage';

export const DiagnosticsPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const [searchParams] = useSearchParams();
  const [runtimeOpen, setRuntimeOpen] = useState(searchParams.get('section') === 'runtime');
  const [report, setReport] = useState<DiagnosticReport | null>(null);
  const [loading, setLoading] = useState(true);

  // HS3 Spectator Live Diagnostics State
  const [handPayload, setHandPayload] = useState<SpectatorHandPayload | null>(null);
  const [bridgeStatus, setBridgeStatus] = useState<BridgeConnectionState | null>(null);
  const [pollingActive, setPollingActive] = useState(true);
  const [spectatorLoading, setSpectatorLoading] = useState(false);

  const fetchSpectatorData = useCallback(async () => {
    try {
      const [payload, bStatus] = await Promise.all([
        api.getSpectatorHandPayload(),
        api.getBridgeStatus(),
      ]);
      setHandPayload(payload);
      setBridgeStatus(bStatus);
    } catch (err) {
      console.error('Failed to fetch spectator diagnostic data', err);
    }
  }, []);

  const handleManualSpectatorRefresh = async () => {
    setSpectatorLoading(true);
    await fetchSpectatorData();
    setSpectatorLoading(false);
  };

  useEffect(() => {
    fetchSpectatorData();
    if (!pollingActive) return;
    const interval = setInterval(fetchSpectatorData, 1000);
    return () => clearInterval(interval);
  }, [pollingActive, fetchSpectatorData]);

  useEffect(() => {
    let unlisten: (() => void) | undefined;
    listen<SpectatorHandPayload>('spectator://hand-snapshot', (event) => {
      if (event.payload) {
        setHandPayload(event.payload);
      }
    })
      .then((fn) => {
        unlisten = fn;
      })
      .catch((err) => {
        console.warn('Failed to listen to spectator://hand-snapshot', err);
      });

    return () => {
      if (unlisten) {
        unlisten();
      }
    };
  }, []);

  const runDiagnostics = () => {
    setLoading(true);
    api.getDiagnosticReport()
      .then(setReport)
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    runDiagnostics();
  }, []);

  useEffect(() => {
    if (searchParams.get('section') === 'runtime') setRuntimeOpen(true);
  }, [searchParams]);

  const isZh = locale === 'zh-CN';

  const isTcp = bridgeStatus?.activeTransport === 'LOOPBACK_TCP' || bridgeStatus?.tcpConnected;
  const isPipe = bridgeStatus?.activeTransport === 'NAMED_PIPE_LEGACY' || (bridgeStatus?.namedPipeConnected && !isTcp);
  const isBridgeConnected = !!bridgeStatus?.runtimeConnected && (isTcp || isPipe);

  const transportLabel = isBridgeConnected
    ? (isTcp
      ? (isZh ? '已连接 (Loopback TCP)' : 'Connected (Loopback TCP)')
      : (isPipe
        ? (isZh ? '已连接 (Named Pipe)' : 'Connected (Named Pipe)')
        : (isZh ? '已连接' : 'Connected')))
    : (isZh ? '未连接' : 'Disconnected');

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('diagnostics.title')}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('diagnostics.subtitle')}
          </p>
        </div>
        <button className="btn btn-primary" onClick={runDiagnostics} disabled={loading}>
          <RefreshCw size={14} className={loading ? 'spin' : ''} />
          <span>{t('diagnostics.runProbe')}</span>
        </button>
      </div>

      <details className="panel" open={runtimeOpen} onToggle={event => setRuntimeOpen(event.currentTarget.open)}>
        <summary style={{ cursor: 'pointer', fontWeight: 600 }}>游戏组件检查 · 文件、进程与连接</summary>
        {runtimeOpen && <div style={{ marginTop: 20 }}><RuntimeDiagnosticsPanel /></div>}
      </details>

      {report && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
          {/* Status Banner */}
          <div
            className="panel"
            style={{
              borderLeft: `4px solid ${
                report.overall_status === 'HEALTHY'
                  ? 'var(--success)'
                  : report.overall_status === 'WARNING'
                  ? 'var(--warning)'
                  : 'var(--danger)'
              }`,
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div>
                <div style={{ fontSize: 18, fontWeight: 700 }}>
                  {t('dashboard.systemStatus')}: {report.overall_status}
                </div>
                <div style={{ fontSize: 13, color: 'var(--text-secondary)', marginTop: 4 }}>
                  {report.items.filter((i) => i.severity === 'OK').length} {isZh ? '项检查通过' : 'checks passed'} |{' '}
                  {report.items.filter((i) => i.severity === 'WARNING').length} {isZh ? '项警告' : 'warnings'} |{' '}
                  {report.items.filter((i) => i.severity === 'ERROR').length} {isZh ? '项错误' : 'errors'}
                </div>
              </div>
              <div
                className={`status-pill ${report.overall_status.toLowerCase()}`}
                style={{ fontSize: 13, padding: '6px 14px' }}
              >
                <span className="pulse-dot" />
                <span>{report.overall_status}</span>
              </div>
            </div>
          </div>

          {/* Rule Evaluation Items */}
          <div className="panel">
            <h2 className="panel-title" style={{ marginBottom: 16 }}>
              <Activity size={18} color="var(--primary)" />
              <span>{isZh ? '诊断规则评估' : 'Diagnostic Rules Evaluation'}</span>
            </h2>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {report.items.map((item) => {
                const Icon =
                  item.severity === 'OK'
                    ? CheckCircle2
                    : item.severity === 'WARNING'
                    ? AlertTriangle
                    : item.severity === 'ERROR'
                    ? AlertCircle
                    : Info;

                const color =
                  item.severity === 'OK'
                    ? 'var(--success)'
                    : item.severity === 'WARNING'
                    ? 'var(--warning)'
                    : item.severity === 'ERROR'
                    ? 'var(--danger)'
                    : 'var(--info)';

                return (
                  <div
                    key={item.rule_id}
                    style={{
                      padding: 16,
                      borderRadius: 'var(--radius-sm)',
                      backgroundColor: 'var(--bg-secondary)',
                      border: '1px solid var(--border-color)',
                      display: 'flex',
                      gap: 14,
                      alignItems: 'flex-start',
                    }}
                  >
                    <Icon size={20} color={color} style={{ marginTop: 2, flexShrink: 0 }} />
                    <div style={{ flex: 1 }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <span style={{ fontWeight: 600, fontSize: 14 }}>{item.title}</span>
                        <span className={`status-pill ${item.severity.toLowerCase()}`} style={{ fontSize: 11, padding: '2px 8px' }}>
                          {item.severity}
                        </span>
                      </div>
                      <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginTop: 4 }}>
                        {item.description}
                      </p>
                      {item.recommendation && (
                        <div
                          style={{
                            marginTop: 8,
                            padding: '6px 10px',
                            borderRadius: 4,
                            backgroundColor: 'rgba(255, 255, 255, 0.03)',
                            fontSize: 12,
                            color: 'var(--text-primary)',
                          }}
                        >
                          <strong>{isZh ? '建议' : 'Recommendation'}:</strong> {item.recommendation}
                        </div>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Environment Snapshot */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 20 }}>
            <div className="panel">
              <h2 className="panel-title">
                <Cpu size={18} color="var(--accent)" />
                <span>{isZh ? '系统与进程快照' : 'Process & OS Snapshot'}</span>
              </h2>
              <div className="code-box">
                <div><strong>{isZh ? '操作系统' : 'Operating System'}:</strong> {report.snapshot.os_name} ({report.snapshot.os_version})</div>
                <div style={{ marginTop: 8 }}>
                  <strong>{isZh ? '运行中游戏进程' : 'Running Game Processes'}:</strong>
                  {report.snapshot.running_processes.length === 0 ? (
                    <span style={{ color: 'var(--success)', marginLeft: 6 }}>{isZh ? '无 (安全)' : 'None (Safe)'}</span>
                  ) : (
                    <ul style={{ marginLeft: 20, marginTop: 4 }}>
                      {report.snapshot.running_processes.map((proc, i) => (
                        <li key={i} style={{ color: 'var(--warning)' }}>{proc}</li>
                      ))}
                    </ul>
                  )}
                </div>
              </div>
            </div>

            <div className="panel">
              <h2 className="panel-title">
                <HardDrive size={18} color="var(--info)" />
                <span>{isZh ? '运行时快照' : 'Runtime Snapshot'}</span>
              </h2>
              <div className="code-box">
                <div><strong>{isZh ? '运行时存在' : 'XingShu Present'}:</strong> {report.snapshot.xingshu_detected ? (isZh ? '是' : 'Yes') : (isZh ? '否' : 'No')}</div>
                <div><strong>{isZh ? '运行时路径' : 'XingShu Path'}:</strong> {report.snapshot.xingshu_path || (isZh ? '无' : 'None')}</div>
                <div style={{ marginTop: 8 }}>
                  <strong>{isZh ? '近期事务数' : 'Recent Transactions'}:</strong> {report.snapshot.recent_transactions.length}
                </div>
              </div>
            </div>
          </div>

          {/* HS3 Spectator Live Diagnostics Dev Section */}
          <div className="panel" style={{ borderTop: '3px solid var(--accent)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
              <h2 className="panel-title" style={{ margin: 0 }}>
                <Eye size={18} color="var(--accent)" />
                <span>{isZh ? 'HS3 观战实时诊断 (HS3 Spectator Live Diagnostics)' : 'HS3 Spectator Live Diagnostics'}</span>
              </h2>
              <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
                <button
                  className="btn btn-secondary"
                  style={{ fontSize: 12, padding: '4px 10px' }}
                  onClick={() => setPollingActive(!pollingActive)}
                >
                  <Radio size={12} color={pollingActive ? 'var(--success)' : 'var(--text-secondary)'} />
                  <span>{pollingActive ? (isZh ? '1s 轮询中 (开启)' : '1s Polling: ON') : (isZh ? '1s 轮询 (已暂停)' : '1s Polling: OFF')}</span>
                </button>
                <button
                  className="btn btn-primary"
                  style={{ fontSize: 12, padding: '4px 10px' }}
                  onClick={handleManualSpectatorRefresh}
                  disabled={spectatorLoading}
                >
                  <RefreshCw size={12} className={spectatorLoading ? 'spin' : ''} />
                  <span>{isZh ? '刷新手牌' : 'Refresh'}</span>
                </button>
              </div>
            </div>

            {/* Bridge & Session Meta Grid */}
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
                gap: 12,
                marginBottom: 16,
              }}
            >
              <div className="code-box" style={{ padding: '8px 12px' }}>
                <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>{isZh ? '桥接通信状态' : 'Bridge Connected'}</div>
                <div style={{ marginTop: 4, display: 'flex', alignItems: 'center', gap: 6, fontWeight: 600 }}>
                  <span
                    style={{
                      display: 'inline-block',
                      width: 8,
                      height: 8,
                      borderRadius: '50%',
                      backgroundColor: isBridgeConnected ? 'var(--success)' : 'var(--danger)',
                    }}
                  />
                  <span>
                    {transportLabel}
                  </span>
                </div>
              </div>

              <div className="code-box" style={{ padding: '8px 12px' }}>
                <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>{isZh ? '房间 ID (Room ID)' : 'Room ID'}</div>
                <div style={{ marginTop: 4, fontWeight: 600 }}>
                  {handPayload ? handPayload.roomId : (isZh ? '无活跃房间' : 'None')}
                </div>
              </div>

              <div className="code-box" style={{ padding: '8px 12px' }}>
                <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>{isZh ? '观战纪元 (Session Epoch)' : 'Session Epoch'}</div>
                <div style={{ marginTop: 4, fontWeight: 600 }}>
                  {handPayload ? handPayload.spectatorSessionEpoch : 0}
                </div>
              </div>

              <div className="code-box" style={{ padding: '8px 12px' }}>
                <div style={{ fontSize: 11, color: 'var(--text-secondary)' }}>{isZh ? '序列号 (Sequence Number)' : 'Sequence Number'}</div>
                <div style={{ marginTop: 4, fontWeight: 600 }}>
                  {handPayload ? `#${handPayload.sequenceNumber}` : (isZh ? '无' : 'None')}
                </div>
              </div>
            </div>

            {/* Spectator Snapshot Diagnostics Table */}
            <div style={{ marginBottom: 20 }}>
              <div style={{ fontSize: 13, fontWeight: 600, marginBottom: 8, color: 'var(--text-secondary)' }}>
                {isZh ? '观战手牌快照诊断表 (Spectator Hand Snapshot Diagnostics Table)' : 'Spectator Hand Snapshot Diagnostics Table'}
              </div>

              {handPayload ? (
                <div style={{ overflowX: 'auto', border: '1px solid var(--border-color)', borderRadius: 'var(--radius-sm)' }}>
                  <table className="data-table" style={{ width: '100%' }}>
                    <thead>
                      <tr>
                        <th>{isZh ? '房间 ID (Room ID)' : 'Room ID'}</th>
                        <th>{isZh ? '纪元 (Epoch)' : 'Epoch'}</th>
                        <th>{isZh ? '序列号 (Sequence)' : 'Sequence'}</th>
                        <th>{isZh ? '玩家 ID (Player ID)' : 'Player ID'}</th>
                        <th>{isZh ? '手牌数 (Hand Size)' : 'Hand Size'}</th>
                        <th>{isZh ? '卡牌 ID (Card IDs)' : 'Card IDs'}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {handPayload.roomPlayers && Object.keys(handPayload.roomPlayers).length > 0 ? (
                        Object.entries(handPayload.roomPlayers).map(([seatKey, p]) => {
                          const isSubject = p.playerId === handPayload.playerId;
                          return (
                            <tr key={seatKey} style={isSubject ? { backgroundColor: 'rgba(59, 130, 246, 0.08)' } : undefined}>
                              <td><code>{handPayload.roomId}</code></td>
                              <td><span className="status-pill ok" style={{ fontSize: 11, padding: '2px 8px' }}>{handPayload.spectatorSessionEpoch}</span></td>
                              <td><code>#{handPayload.sequenceNumber}</code></td>
                              <td>
                                <code>{p.playerId}</code>
                                {isSubject && (
                                  <span style={{ marginLeft: 6, fontSize: 11, color: 'var(--primary)', fontWeight: 600 }}>
                                    {isZh ? '(当前事件)' : '(Current)'}
                                  </span>
                                )}
                              </td>
                              <td><strong>{p.handSize}</strong></td>
                              <td>
                                <code>
                                  {p.cardIds && p.cardIds.length > 0
                                    ? `[${p.cardIds.join(', ')}]`
                                    : '[]'}
                                </code>
                              </td>
                            </tr>
                          );
                        })
                      ) : (
                        <tr>
                          <td><code>{handPayload.roomId}</code></td>
                          <td><span className="status-pill ok" style={{ fontSize: 11, padding: '2px 8px' }}>{handPayload.spectatorSessionEpoch}</span></td>
                          <td><code>#{handPayload.sequenceNumber}</code></td>
                          <td><code>{handPayload.playerId}</code></td>
                          <td><strong>{handPayload.handSize ?? (handPayload.cardIds?.length ?? 0)}</strong></td>
                          <td>
                            <code>
                              {handPayload.cardIds && handPayload.cardIds.length > 0
                                ? `[${handPayload.cardIds.join(', ')}]`
                                : '[]'}
                            </code>
                          </td>
                        </tr>
                      )}
                    </tbody>
                  </table>
                </div>
              ) : (
                <div
                  style={{
                    padding: '16px',
                    textAlign: 'center',
                    backgroundColor: 'rgba(255, 255, 255, 0.02)',
                    borderRadius: 6,
                    color: 'var(--text-secondary)',
                    fontSize: 13,
                  }}
                >
                  {isZh
                    ? '暂无活跃观战手牌快照 (等待游戏进入对局或 Bridge 广播事件...)'
                    : 'No active spectator hand snapshot available. Waiting for match or bridge event...'}
                </div>
              )}
            </div>

            {/* 4 Watched Players Hand Info */}
            <div>
              <div style={{ fontSize: 13, fontWeight: 600, marginBottom: 8, color: 'var(--text-secondary)' }}>
                {isZh ? '房间 4 人全景手牌快照 (4 Watched Players)' : '4 Watched Players Hand Snapshot'}
              </div>

              {handPayload && Object.keys(handPayload.roomPlayers || {}).length > 0 ? (
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 12 }}>
                  {Object.entries(handPayload.roomPlayers).map(([seatKey, p]) => (
                    <div
                      key={seatKey}
                      className="code-box"
                      style={{
                        padding: '12px',
                        border: '1px solid rgba(255, 255, 255, 0.08)',
                        borderRadius: 6,
                        backgroundColor: 'rgba(0, 0, 0, 0.25)',
                      }}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
                        <span style={{ fontWeight: 700, color: 'var(--accent)' }}>
                          {isZh ? `玩家席位 ${seatKey}` : `Seat ${seatKey}`}
                        </span>
                        <span className="status-pill" style={{ fontSize: 11, padding: '2px 8px' }}>
                          {isZh ? `手牌数: ${p.handSize}` : `Hand Size: ${p.handSize}`}
                        </span>
                      </div>
                      <div style={{ fontSize: 12, marginBottom: 4 }}>
                        <span style={{ color: 'var(--text-secondary)' }}>Player ID: </span>
                        <code>{p.playerId}</code>
                      </div>
                      <div style={{ fontSize: 12, marginBottom: 6 }}>
                        <span style={{ color: 'var(--text-secondary)' }}>Card IDs: </span>
                        <code>[{p.cardIds.join(', ')}]</code>
                      </div>
                      <div style={{ fontSize: 12, borderTop: '1px dashed rgba(255, 255, 255, 0.1)', paddingTop: 6 }}>
                        <span style={{ color: 'var(--text-secondary)' }}>Multiset Cards: </span>
                        {p.cards && p.cards.length > 0 ? (
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4, marginTop: 4 }}>
                            {p.cards.map((c) => (
                              <span
                                key={c.cardId}
                                style={{
                                  fontSize: 11,
                                  backgroundColor: 'rgba(255, 255, 255, 0.08)',
                                  padding: '2px 6px',
                                  borderRadius: 4,
                                }}
                              >
                                {c.cardId} x{c.count}
                              </span>
                            ))}
                          </div>
                        ) : (
                          <span style={{ color: 'var(--text-secondary)', fontStyle: 'italic' }}> (empty)</span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <div
                  style={{
                    padding: '20px',
                    textAlign: 'center',
                    backgroundColor: 'rgba(255, 255, 255, 0.02)',
                    borderRadius: 6,
                    color: 'var(--text-secondary)',
                    fontSize: 13,
                  }}
                >
                  {isZh
                    ? '暂无活跃观战手牌快照 (等待游戏进入对局或 Bridge 广播事件...)'
                    : 'No active spectator hand snapshot available. Waiting for match or bridge event...'}
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
