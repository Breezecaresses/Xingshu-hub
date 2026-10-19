import React, { useEffect, useState, useRef } from 'react';
import { api } from '../../shared/api/tauri';
import type { BridgeConnectionState, BridgeLogEntry } from '../../shared/types';
import {
  Terminal,
  Server,
  Zap,
  Copy,
  ShieldCheck,
  Send,
  RefreshCw,
  Trash2,
  CheckCircle2,
  AlertCircle,
  Clock,
  Cpu,
  Radio,
  ChevronDown,
  ChevronUp,
} from 'lucide-react';
import { useTranslation } from '../../i18n';

export const BridgePage: React.FC = () => {
  const { locale, t } = useTranslation();
  const isZh = locale === 'zh-CN';
  const [status, setStatus] = useState<BridgeConnectionState | null>(null);
  const [copied, setCopied] = useState(false);
  const [testMessage, setTestMessage] = useState(isZh ? '来自星枢 Hub 前端的 Ping 信号' : 'Ping from XingShu Hub Frontend');
  const [isSending, setIsSending] = useState(false);
  const [feedback, setFeedback] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [expandedLogId, setExpandedLogId] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);

  const timerRef = useRef<number | null>(null);

  const fetchStatus = async () => {
    try {
      const s = await api.getBridgeStatus();
      setStatus(s);
    } catch (e) {
      console.error('Failed to fetch bridge status:', e);
    }
  };

  useEffect(() => {
    fetchStatus();
    if (autoRefresh) {
      timerRef.current = window.setInterval(fetchStatus, 1500);
    }
    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
    };
  }, [autoRefresh]);

  const copyPipeName = () => {
    const isTcp = status?.activeTransport === 'LOOPBACK_TCP' || status?.tcpConnected;
    const target = isTcp ? (status?.tcpAddr || '127.0.0.1:25890') : (status?.namedPipe || '\\\\.\\pipe\\XingShuBridge_v1');
    navigator.clipboard.writeText(target);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleSendTestEvent = async () => {
    setIsSending(true);
    setFeedback(null);
    try {
      await api.sendBridgeTestEvent(testMessage);
      setFeedback({ type: 'success', text: t('bridge.testSuccess') });
      await fetchStatus();
    } catch (e: any) {
      const errStr = typeof e === 'string' ? e : e?.message || JSON.stringify(e);
      setFeedback({
        type: 'error',
        text: `${t('bridge.testFailed')}: ${errStr} ${t('bridge.testFailedHint')}`,
      });
      await fetchStatus();
    } finally {
      setIsSending(false);
    }
  };

  const handleClearLogs = async () => {
    try {
      await api.clearBridgeLogs();
      await fetchStatus();
    } catch (e) {
      console.error('Failed to clear logs:', e);
    }
  };

  const formatTime = (ts?: number) => {
    if (!ts) return t('common.unknown');
    const d = new Date(ts);
    return d.toTimeString().split(' ')[0] + '.' + String(d.getMilliseconds()).padStart(3, '0');
  };

  const formatAgo = (ts?: number) => {
    if (!ts) return '';
    const diff = Math.max(0, Math.floor((Date.now() - ts) / 1000));
    if (diff < 5) return t('common.justNow');
    if (diff < 60) return isZh ? `${diff} 秒前` : `${diff}s ago`;
    return isZh ? `${Math.floor(diff / 60)} 分钟前` : `${Math.floor(diff / 60)}m ago`;
  };

  const isConnected = !!status?.runtimeConnected;

  return (
    <div style={{ maxWidth: 1200, margin: '0 auto', paddingBottom: 40 }}>
      {/* Top Header */}
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
            <h1 style={{ fontSize: 22, fontWeight: 700, margin: 0 }}>{t('bridge.title')}</h1>
            <span
              style={{
                fontSize: 11,
                fontWeight: 600,
                padding: '2px 8px',
                borderRadius: 4,
                background: 'rgba(56, 189, 248, 0.15)',
                color: '#38bdf8',
                border: '1px solid rgba(56, 189, 248, 0.3)',
              }}
            >
              Protocol v{status?.protocolVersion || '0.1'}
            </span>
          </div>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('bridge.subtitle')}
          </p>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <button
            onClick={() => setAutoRefresh(!autoRefresh)}
            className="btn btn-secondary"
            style={{ fontSize: 12, display: 'flex', alignItems: 'center', gap: 6, padding: '6px 12px' }}
          >
            <Radio size={14} color={autoRefresh ? 'var(--success)' : 'var(--text-muted)'} />
            {autoRefresh ? t('bridge.autoPoll') : t('bridge.pollingPaused')}
          </button>
          <button
            onClick={fetchStatus}
            className="btn btn-secondary"
            style={{ fontSize: 12, display: 'flex', alignItems: 'center', gap: 6, padding: '6px 12px' }}
          >
            <RefreshCw size={14} />
            {t('bridge.refreshNow')}
          </button>
        </div>
      </div>

      {/* Status Cards */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
          gap: 16,
          marginBottom: 20,
        }}
      >
        {/* Card 1: Runtime Transport Status */}
        <div className="panel" style={{ margin: 0 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
              {status?.activeTransport === 'LOOPBACK_TCP' || status?.tcpConnected
                ? (locale === 'zh-CN' ? 'Runtime TCP 通信状态' : 'Runtime TCP Status')
                : t('bridge.runtimePipeStatus')}
            </div>
            <span
              style={{
                fontSize: 11,
                padding: '2px 8px',
                borderRadius: 12,
                fontWeight: 600,
                background: isConnected ? 'rgba(34, 197, 94, 0.15)' : 'rgba(234, 179, 8, 0.15)',
                color: isConnected ? '#4ade80' : '#facc15',
                border: `1px solid ${isConnected ? 'rgba(34, 197, 94, 0.3)' : 'rgba(234, 179, 8, 0.3)'}`,
              }}
            >
              {isConnected
                ? (status?.activeTransport === 'LOOPBACK_TCP' || status?.tcpConnected
                  ? (locale === 'zh-CN' ? '已连接 (TCP)' : 'Connected (TCP)')
                  : (status?.activeTransport === 'NAMED_PIPE_LEGACY' || status?.namedPipeConnected
                    ? (locale === 'zh-CN' ? '已连接 (Pipe)' : 'Connected (Pipe)')
                    : t('bridge.connectedBadge')))
                : t('bridge.disconnectedBadge')}
            </span>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 10 }}>
            <span
              className={isConnected ? 'pulse-dot' : ''}
              style={{
                display: 'inline-block',
                width: 10,
                height: 10,
                borderRadius: '50%',
                background: isConnected ? '#22c55e' : '#eab308',
              }}
            />
            <span style={{ fontSize: 16, fontWeight: 700, color: '#fff' }}>
              {isConnected ? t('bridge.duplexActive') : t('bridge.waitingGame')}
            </span>
          </div>

          <div
            style={{
              fontSize: 11,
              fontFamily: 'var(--font-mono)',
              color: 'var(--text-secondary)',
              marginTop: 10,
              display: 'flex',
              alignItems: 'center',
              gap: 6,
              background: 'var(--bg-secondary)',
              padding: '6px 8px',
              borderRadius: 4,
            }}
          >
            <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {status?.activeTransport === 'LOOPBACK_TCP' || status?.tcpConnected
                ? `${status?.tcpAddr || '127.0.0.1:25890'} (Loopback TCP)`
                : (status?.namedPipe || '\\\\.\\pipe\\XingShuBridge_v1')}
            </span>
            <button
              onClick={copyPipeName}
              style={{ background: 'none', border: 'none', color: 'var(--primary)', cursor: 'pointer', padding: 0 }}
              title={status?.activeTransport === 'LOOPBACK_TCP' || status?.tcpConnected ? 'Copy TCP Endpoint' : t('bridge.copyPipe')}
            >
              <Copy size={13} />
            </button>
            {copied && <span style={{ color: 'var(--success)', fontSize: 10 }}>{t('bridge.copied')}</span>}
          </div>

          <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 8 }}>
            {t('bridge.stats')}: {t('bridge.received')} {status?.totalMessagesReceived || 0} {t('bridge.packets')} | {t('bridge.sent')} {status?.totalMessagesSent || 0} {t('bridge.packets')}
          </div>
        </div>

        {/* Card 2: Heartbeat & Latency */}
        <div className="panel" style={{ margin: 0 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{t('bridge.heartbeatTitle')}</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 10 }}>
            <Zap size={18} color="var(--primary)" />
            <span style={{ fontSize: 16, fontWeight: 700, color: '#fff' }}>
              {status?.lastHeartbeatLatencyMs !== undefined && status?.lastHeartbeatLatencyMs !== null
                ? `${status.lastHeartbeatLatencyMs} ms`
                : isConnected
                ? '< 0.5 ms'
                : 'N/A'}
            </span>
            <span style={{ fontSize: 11, color: 'var(--text-secondary)' }}>
              {status?.lastHeartbeatTimestamp ? `(${formatAgo(status.lastHeartbeatTimestamp)})` : ''}
            </span>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, marginTop: 10, fontSize: 11 }}>
            <div style={{ background: 'var(--bg-secondary)', padding: '6px 8px', borderRadius: 4 }}>
              <span style={{ color: 'var(--text-muted)' }}>{t('bridge.lastHeartbeat')}: </span>
              <span style={{ color: '#fff', fontFamily: 'var(--font-mono)' }}>
                {status?.lastHeartbeatTimestamp ? formatTime(status.lastHeartbeatTimestamp) : '--'}
              </span>
            </div>
            <div style={{ background: 'var(--bg-secondary)', padding: '6px 8px', borderRadius: 4 }}>
              <span style={{ color: 'var(--text-muted)' }}>{t('bridge.connectedAt')}: </span>
              <span style={{ color: '#fff', fontFamily: 'var(--font-mono)' }}>
                {status?.connectedAt ? formatTime(status.connectedAt) : '--'}
              </span>
            </div>
          </div>

          <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 8 }}>
            {t('bridge.heartbeatInterval')}
          </div>
        </div>

        {/* Card 3: Game Process & Runtime Metadata */}
        <div className="panel" style={{ margin: 0 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{t('bridge.metadataTitle')}</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 10 }}>
            <Cpu size={18} color="var(--accent)" />
            <span style={{ fontSize: 16, fontWeight: 700, color: '#fff' }}>
              {status?.gamePid ? `PID: ${status.gamePid}` : t('bridge.noTargetProcess')}
            </span>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, marginTop: 10, fontSize: 11 }}>
            <div style={{ background: 'var(--bg-secondary)', padding: '6px 8px', borderRadius: 4 }}>
              <span style={{ color: 'var(--text-muted)' }}>{t('bridge.gameVersion')}: </span>
              <span style={{ color: '#fff', fontWeight: 600 }}>{status?.gameVersion || t('bridge.unknown')}</span>
            </div>
            <div style={{ background: 'var(--bg-secondary)', padding: '6px 8px', borderRadius: 4 }}>
              <span style={{ color: 'var(--text-muted)' }}>{t('bridge.runtimeVersion')}: </span>
              <span style={{ color: 'var(--accent)', fontWeight: 600 }}>
                {status?.runtimeVersion || '1.0.0-rc1.1'}
              </span>
            </div>
          </div>

          <div style={{ fontSize: 11, color: 'var(--text-secondary)', marginTop: 8 }}>
            {t('bridge.hostComponent')}
          </div>
        </div>
      </div>

      {/* Manual Test Event Injection Panel */}
      <div className="panel" style={{ marginBottom: 20 }}>
        <div className="panel-header">
          <div className="panel-title">
            <Send size={18} color="var(--primary)" />
            <span>{t('bridge.testPanelTitle')}</span>
          </div>
          <span className="badge badge-success">
            <ShieldCheck size={12} style={{ marginRight: 4 }} />
            {t('bridge.singleUserAcl')}
          </span>
        </div>

        <p style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 12 }}>
          {t('bridge.testPanelDesc')}
        </p>

        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
          <input
            type="text"
            className="input"
            value={testMessage}
            onChange={(e) => setTestMessage(e.target.value)}
            placeholder={t('bridge.testPlaceholder')}
            style={{ flex: '1 1 300px', minWidth: 200 }}
          />
          <button
            onClick={handleSendTestEvent}
            disabled={isSending}
            className="btn btn-primary"
            style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 140, justifyContent: 'center' }}
          >
            <Send size={14} />
            {isSending ? t('bridge.sending') : t('bridge.sendTest')}
          </button>
        </div>

        {feedback && (
          <div
            style={{
              marginTop: 12,
              padding: '8px 12px',
              borderRadius: 'var(--radius-sm)',
              fontSize: 12,
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              background: feedback.type === 'success' ? 'rgba(34, 197, 94, 0.15)' : 'rgba(239, 68, 68, 0.15)',
              color: feedback.type === 'success' ? '#4ade80' : '#f87171',
              border: `1px solid ${feedback.type === 'success' ? 'rgba(34, 197, 94, 0.3)' : 'rgba(239, 68, 68, 0.3)'}`,
            }}
          >
            {feedback.type === 'success' ? <CheckCircle2 size={16} /> : <AlertCircle size={16} />}
            <span>{feedback.text}</span>
          </div>
        )}
      </div>

      {/* Real-time Message Log Stream */}
      <div className="panel" style={{ marginBottom: 20 }}>
        <div className="panel-header" style={{ marginBottom: 12 }}>
          <div className="panel-title">
            <Terminal size={18} color="var(--primary)" />
            <span>{t('bridge.liveLogTitle')}</span>
            <span style={{ fontSize: 11, color: 'var(--text-muted)', marginLeft: 8 }}>
              ({t('bridge.showingRecent')} {status?.recentMessages?.length || 0} {t('bridge.entries')})
            </span>
          </div>
          <button
            onClick={handleClearLogs}
            className="btn btn-secondary"
            style={{ fontSize: 11, padding: '4px 8px', display: 'flex', alignItems: 'center', gap: 4 }}
          >
            <Trash2 size={12} />
            {t('bridge.clearLogs')}
          </button>
        </div>

        {(!status?.recentMessages || status.recentMessages.length === 0) ? (
          <div
            style={{
              textAlign: 'center',
              padding: '36px 16px',
              color: 'var(--text-muted)',
              fontSize: 13,
              background: 'var(--bg-secondary)',
              borderRadius: 'var(--radius-sm)',
              border: '1px dashed var(--border-color)',
            }}
          >
            <Clock size={24} style={{ display: 'block', margin: '0 auto 8px', opacity: 0.5 }} />
            {t('bridge.noLogs')}
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {status.recentMessages.slice().reverse().map((entry: BridgeLogEntry) => {
              const isExpanded = expandedLogId === entry.id;
              const isOut = entry.direction === 'OUT';
              const isSys = entry.direction === 'SYS';

              return (
                <div
                  key={entry.id}
                  style={{
                    background: 'var(--bg-secondary)',
                    borderRadius: 'var(--radius-sm)',
                    border: '1px solid var(--border-color)',
                    fontSize: 12,
                    overflow: 'hidden',
                  }}
                >
                  <div
                    onClick={() => setExpandedLogId(isExpanded ? null : entry.id)}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '8px 12px',
                      cursor: 'pointer',
                      gap: 10,
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flex: 1, minWidth: 0 }}>
                      <span
                        style={{
                          fontSize: 10,
                          fontWeight: 700,
                          padding: '1px 6px',
                          borderRadius: 3,
                          fontFamily: 'var(--font-mono)',
                          background: isSys
                            ? 'rgba(234, 179, 8, 0.2)'
                            : isOut
                            ? 'rgba(168, 85, 247, 0.2)'
                            : 'rgba(56, 189, 248, 0.2)',
                          color: isSys ? '#facc15' : isOut ? '#c084fc' : '#38bdf8',
                        }}
                      >
                        {entry.direction}
                      </span>

                      <span style={{ fontWeight: 600, color: '#fff', fontFamily: 'var(--font-mono)' }}>
                        {entry.messageType}
                      </span>

                      <span
                        style={{
                          color: 'var(--text-secondary)',
                          fontSize: 11,
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                          flex: 1,
                        }}
                      >
                        {entry.summary}
                      </span>
                    </div>

                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, fontSize: 11, color: 'var(--text-muted)' }}>
                      <span style={{ fontFamily: 'var(--font-mono)' }}>{formatTime(entry.timestamp)}</span>
                      {isExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                    </div>
                  </div>

                  {isExpanded && entry.rawJson && (
                    <div style={{ padding: '0 12px 10px', background: 'var(--bg-primary)' }}>
                      <pre
                        style={{
                          margin: 0,
                          padding: 8,
                          fontSize: 11,
                          fontFamily: 'var(--font-mono)',
                          color: 'var(--text-secondary)',
                          background: 'rgba(0,0,0,0.3)',
                          borderRadius: 4,
                          overflowX: 'auto',
                          maxHeight: 180,
                        }}
                      >
                        {(() => {
                          try {
                            return JSON.stringify(JSON.parse(entry.rawJson), null, 2);
                          } catch {
                            return entry.rawJson;
                          }
                        })()}
                      </pre>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* IPC Protocol Architecture Info Box */}
      <div className="panel" style={{ margin: 0 }}>
        <div className="panel-header">
          <div className="panel-title">
            <Server size={18} color="var(--primary)" />
            <span>{t('bridge.archTitle')}</span>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 14, fontSize: 12 }}>
          <div style={{ background: 'var(--bg-secondary)', padding: 12, borderRadius: 'var(--radius-sm)' }}>
            <div style={{ fontWeight: 600, color: 'var(--primary)', marginBottom: 4 }}>
              {t('bridge.secItem1Title')}
            </div>
            <p style={{ color: 'var(--text-secondary)', margin: 0, lineHeight: 1.5 }}>
              {t('bridge.secItem1Desc')}
            </p>
          </div>

          <div style={{ background: 'var(--bg-secondary)', padding: 12, borderRadius: 'var(--radius-sm)' }}>
            <div style={{ fontWeight: 600, color: 'var(--accent)', marginBottom: 4 }}>
              {t('bridge.secItem2Title')}
            </div>
            <p style={{ color: 'var(--text-secondary)', margin: 0, lineHeight: 1.5 }}>
              {t('bridge.secItem2Desc')}
            </p>
          </div>

          <div style={{ background: 'var(--bg-secondary)', padding: 12, borderRadius: 'var(--radius-sm)' }}>
            <div style={{ fontWeight: 600, color: 'var(--success)', marginBottom: 4 }}>
              {t('bridge.secItem3Title')}
            </div>
            <p style={{ color: 'var(--text-secondary)', margin: 0, lineHeight: 1.5 }}>
              {t('bridge.secItem3Desc')}
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};
