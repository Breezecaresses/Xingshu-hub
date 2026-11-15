import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  ChevronDown,
  ChevronRight,
  Link2,
  Loader2,
  Sparkles,
  Star,
  Trash2,
  Upload,
} from 'lucide-react';
import {
  analyzeReplayById,
  uploadReplayFile,
  type ReplayAnalysisReport,
} from '../../shared/webCloudClient';
import {
  DEFAULT_SERVER,
  SAMPLE_REPLAY_ID,
  formatDuration,
  formatTime,
} from './replay/replayShared';
import {
  historyKeyFor,
  loadReplayHistory,
  removeReplayHistory,
  toggleReplayFavorite,
  updateReplayNote,
  upsertReplayHistory,
  type ReplayHistoryRecord,
} from './replay/replayHistory';
import { ReplayOverviewTabs } from './replay/ReplayOverviewTabs';
import { ReplayBoardView } from './replay/ReplayBoardView';
import {
  ReplayChipsTab,
  ReplayExportsTab,
  ReplayFullReportTab,
} from './replay/ReplayChipsAndExports';
import './replay/replay.css';

type ReplayTabKey = 'overview' | 'board' | 'chips' | 'report' | 'export';

export const ReplayAnalyzerPage: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const sessionReportsRef = useRef<Map<string, ReplayAnalysisReport>>(new Map());

  const [server, setServer] = useState(() => searchParams.get('server') || DEFAULT_SERVER);
  const [replayInput, setReplayInput] = useState(() => searchParams.get('mid') || '');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [report, setReport] = useState<ReplayAnalysisReport | null>(null);
  const [activeTab, setActiveTab] = useState<ReplayTabKey>('overview');
  const [boardStepIndex, setBoardStepIndex] = useState(0);
  const [dragging, setDragging] = useState(false);

  const [history, setHistory] = useState<ReplayHistoryRecord[]>(() => loadReplayHistory());
  const [historyOpen, setHistoryOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);
  const [historyTab, setHistoryTab] = useState<'recent' | 'favorite'>('recent');
  const [copiedLink, setCopiedLink] = useState(false);

  const applyReport = useCallback((next: ReplayAnalysisReport, source: 'id' | 'upload') => {
    const key = historyKeyFor(next.replayId, next.server);
    sessionReportsRef.current.set(key, next);
    setReport(next);
    setBoardStepIndex(0);
    setActiveTab('overview');
    setError('');
    setHistory(upsertReplayHistory(next, source));
  }, []);

  const runAnalyzeId = useCallback(
    async (mid: string, srv = server, syncUrl = true) => {
      const clean = mid.trim();
      if (!clean) {
        setError('请输入对局回放编号，或导入本地 .rep 文件');
        return;
      }
      setLoading(true);
      setError('');
      try {
        const res = await analyzeReplayById(clean, srv);
        applyReport(res, 'id');
        if (syncUrl) {
          setSearchParams({ mid: res.replayId, server: res.server || srv }, { replace: true });
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : String(err));
      } finally {
        setLoading(false);
      }
    },
    [applyReport, server, setSearchParams],
  );

  const runUploadFile = useCallback(
    async (file: File) => {
      if (!file.name.toLowerCase().endsWith('.rep')) {
        setError('请选择扩展名为 .rep 的吉星派对回放文件');
        return;
      }
      setLoading(true);
      setError('');
      try {
        const res = await uploadReplayFile(file);
        applyReport(res, 'upload');
      } catch (err) {
        setError(err instanceof Error ? err.message : String(err));
      } finally {
        setLoading(false);
      }
    },
    [applyReport],
  );

  const queryMid = searchParams.get('mid');
  const queryServer = searchParams.get('server') || DEFAULT_SERVER;

  useEffect(() => {
    if (!queryMid) return;
    if (report && report.replayId === queryMid && report.server === queryServer) return;
    setReplayInput(queryMid);
    setServer(queryServer);
    void runAnalyzeId(queryMid, queryServer, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [queryMid, queryServer]);

  const jumpToStep = useCallback((idx: number) => {
    setBoardStepIndex(Math.max(0, idx));
    setActiveTab('board');
  }, []);

  const visibleHistory =
    historyTab === 'favorite' ? history.filter((item) => item.favorite) : history;

  const activeHistoryEntry = report
    ? history.find((item) => item.key === historyKeyFor(report.replayId, report.server))
    : undefined;

  const copyShareUrl = async () => {
    if (!report) return;
    const url = `${window.location.origin}${window.location.pathname}?mid=${encodeURIComponent(
      report.replayId,
    )}&server=${encodeURIComponent(report.server || DEFAULT_SERVER)}`;
    try {
      await navigator.clipboard.writeText(url);
      setCopiedLink(true);
      window.setTimeout(() => setCopiedLink(false), 1800);
    } catch {
      setError(`请手动复制链接：${url}`);
    }
  };

  return (
    <div className="replay-page">
      <section className="replay-hero no-print">
        <div>
          <div className="replay-kicker">REPLAY / 对局复盘</div>
          <h1>从一局回放，看清每一轮。</h1>
          <p className="replay-hero-sub">
            输入对局编号或上传 .rep 文件，直接在网页端深度解析完整战斗输出、前 6 轮代付意愿、逐轮经济来源、筹码三选一与逐轮棋盘行动。
          </p>
        </div>

        <form
          className="replay-lookup-form"
          onSubmit={(e) => {
            e.preventDefault();
            void runAnalyzeId(replayInput, server, true);
          }}
        >
          <div className="replay-lookup-row">
            <select
              className="replay-select"
              value={server}
              onChange={(e) => setServer(e.target.value)}
              aria-label="回放服务器"
              disabled={loading}
            >
              <option value="cn">国服</option>
            </select>

            <input
              type="text"
              className="replay-input"
              placeholder={`输入对局编号，例如 ${SAMPLE_REPLAY_ID}`}
              aria-label="对局回放编号"
              value={replayInput}
              onChange={(e) => setReplayInput(e.target.value)}
              disabled={loading}
            />

            <button type="submit" className="btn btn-primary" disabled={loading}>
              {loading ? <Loader2 size={15} className="spin" /> : <Sparkles size={15} />}
              <span>{loading ? '解析中…' : '分析回放'}</span>
            </button>
          </div>

          <div className="replay-actions-sub">
            <span>编号可在游戏对局记录或 UID 战绩列表中复制</span>
            <button
              type="button"
              className="replay-link-btn"
              disabled={loading}
              onClick={() => {
                setReplayInput(SAMPLE_REPLAY_ID);
                void runAnalyzeId(SAMPLE_REPLAY_ID, 'cn', true);
              }}
            >
              试试示例对局 ({SAMPLE_REPLAY_ID})
            </button>
          </div>

          <div
            className={`replay-dropzone${dragging ? ' dragging' : ''}`}
            onDragOver={(e) => {
              e.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={(e) => {
              e.preventDefault();
              setDragging(false);
              const file = e.dataTransfer.files?.[0];
              if (file) void runUploadFile(file);
            }}
          >
            <input
              ref={fileInputRef}
              type="file"
              accept=".rep"
              style={{ display: 'none' }}
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) void runUploadFile(file);
                e.target.value = '';
              }}
            />
            <button
              type="button"
              className="replay-link-btn"
              disabled={loading}
              onClick={() => fileInputRef.current?.click()}
            >
              <Upload size={14} />
              <span>导入 .rep 回放文件</span>
            </button>
            <span style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
              支持拖拽本地 .rep 文件直接解析
            </span>
          </div>
        </form>
      </section>

      {error && (
        <div
          className="panel no-print"
          role="alert"
          style={{
            borderColor: 'rgba(239, 68, 68, 0.4)',
            background: 'rgba(239, 68, 68, 0.08)',
            color: 'var(--text-primary)',
            fontSize: 13,
          }}
        >
          解析失败：{error}
        </div>
      )}

      <section className="panel replay-drawer no-print">
        <button
          type="button"
          className="replay-drawer-toggle"
          onClick={() => setHistoryOpen((o) => !o)}
        >
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            {historyOpen ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
            <span>本机历史与收藏 ({history.length})</span>
          </span>
          <span style={{ fontSize: 12, color: 'var(--text-secondary)', fontWeight: 400 }}>
            仅保存在当前浏览器，不保留回放原文件
          </span>
        </button>

        {historyOpen && (
          <div style={{ marginTop: 12, display: 'grid', gap: 10 }}>
            <div className="replay-pill-tabs">
              <button
                type="button"
                className={`replay-pill-tab${historyTab === 'recent' ? ' active' : ''}`}
                onClick={() => setHistoryTab('recent')}
              >
                最近 ({history.length})
              </button>
              <button
                type="button"
                className={`replay-pill-tab${historyTab === 'favorite' ? ' active' : ''}`}
                onClick={() => setHistoryTab('favorite')}
              >
                收藏 ({history.filter((h) => h.favorite).length})
              </button>
            </div>

            {visibleHistory.length === 0 ? (
              <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', margin: 0 }}>
                暂无{historyTab === 'favorite' ? '收藏' : '分析'}记录。
              </p>
            ) : (
              <div className="replay-history-list">
                {visibleHistory.map((item) => (
                  <div key={item.key} className="replay-history-item">
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                      <strong>
                        {item.mapName} {item.difficultyName ? `· ${item.difficultyName}` : ''}
                      </strong>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <button
                          type="button"
                          className="btn btn-sm"
                          onClick={() => setHistory(toggleReplayFavorite(item.key))}
                          title={item.favorite ? '取消收藏' : '加入收藏'}
                        >
                          <Star
                            size={13}
                            fill={item.favorite ? '#f59e0b' : 'none'}
                            color={item.favorite ? '#f59e0b' : 'currentColor'}
                          />
                          <span>{item.favorite ? '已收藏' : '收藏'}</span>
                        </button>
                        <button
                          type="button"
                          className="btn btn-sm"
                          onClick={() => setHistory(removeReplayHistory(item.key))}
                          title="删除记录"
                        >
                          <Trash2 size={13} />
                        </button>
                      </div>
                    </div>

                    <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                      {item.source === 'upload' ? '本地导入' : '国服'} · {item.replayId} · {item.rounds} 轮 ·{' '}
                      {item.win ? '胜利' : '失败'} · {formatTime(item.savedAt)}
                    </div>
                    <div style={{ fontSize: 12 }}>{item.playersSummary}</div>

                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center', marginTop: 4 }}>
                      <input
                        type="text"
                        className="replay-input"
                        style={{ flex: '1 1 200px', padding: '5px 9px', fontSize: 12 }}
                        placeholder="添加本局备注…"
                        value={item.note}
                        onChange={(e) => setHistory(updateReplayNote(item.key, e.target.value))}
                      />
                      <button
                        type="button"
                        className="btn btn-primary btn-sm"
                        onClick={() => {
                          const cached = sessionReportsRef.current.get(item.key);
                          if (cached) {
                            setReport(cached);
                            setBoardStepIndex(0);
                            return;
                          }
                          if (item.source === 'upload') {
                            fileInputRef.current?.click();
                            return;
                          }
                          setReplayInput(item.replayId);
                          setServer(item.server || DEFAULT_SERVER);
                          void runAnalyzeId(item.replayId, item.server || DEFAULT_SERVER, true);
                        }}
                      >
                        {item.source === 'upload' && !sessionReportsRef.current.has(item.key)
                          ? '重新选择 .rep'
                          : '载入战报'}
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </section>

      {report && (
        <>
          <section className="panel replay-drawer no-print">
            <button
              type="button"
              className="replay-drawer-toggle"
              onClick={() => setShareOpen((o) => !o)}
            >
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                {shareOpen ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                <span>换局、收藏与分享</span>
              </span>
              <span style={{ fontSize: 12, color: 'var(--text-secondary)', fontWeight: 400 }}>
                复制直达链接或为当前对局添加备注
              </span>
            </button>

            {shareOpen && (
              <div style={{ marginTop: 12, display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
                {report.server !== 'local' && (
                  <button type="button" className="btn btn-sm" onClick={() => void copyShareUrl()}>
                    <Link2 size={14} />
                    <span>{copiedLink ? '已复制分享链接' : '复制复盘直达链接'}</span>
                  </button>
                )}
                {activeHistoryEntry && (
                  <>
                    <button
                      type="button"
                      className="btn btn-sm"
                      onClick={() => setHistory(toggleReplayFavorite(activeHistoryEntry.key))}
                    >
                      <Star
                        size={14}
                        fill={activeHistoryEntry.favorite ? '#f59e0b' : 'none'}
                        color={activeHistoryEntry.favorite ? '#f59e0b' : 'currentColor'}
                      />
                      <span>{activeHistoryEntry.favorite ? '已收藏本局' : '收藏本局'}</span>
                    </button>
                    <input
                      type="text"
                      className="replay-input"
                      style={{ flex: '1 1 240px', padding: '6px 10px', fontSize: 12.5 }}
                      placeholder="为当前对局写一段复盘备注…"
                      value={activeHistoryEntry.note}
                      onChange={(e) =>
                        setHistory(updateReplayNote(activeHistoryEntry.key, e.target.value))
                      }
                    />
                  </>
                )}
              </div>
            )}
          </section>

          <div className="replay-banner-header">
            <div className="replay-banner-title">
              <div>
                <div style={{ fontSize: 12.5, color: 'var(--text-secondary)' }}>
                  对局编号 <code>{report.replayId}</code>
                </div>
                <h2>
                  {report.meta.mapName} · {report.meta.difficultyName}
                </h2>
                <div style={{ fontSize: 13, color: 'var(--text-secondary)', marginTop: 3 }}>
                  {report.meta.rounds} 轮 · {formatDuration(report.meta.durationSec)} ·{' '}
                  <strong style={{ color: report.meta.win ? '#10b981' : '#ef4444' }}>
                    {report.meta.win ? '已结算胜利' : '已结算未通关'}
                  </strong>{' '}
                  · 版本 {report.meta.version}
                </div>
              </div>

              <div className="replay-room-pill">
                <span style={{ color: 'var(--text-secondary)' }}>房间情况</span>
                <span className="replay-badge ok">{report.meta.roomMode}</span>
              </div>
            </div>

            <div className="replay-notice no-print">
              本局已完成全帧与事件块结构化解析（{report.steps.length} 个行动步 · {report.chipEvents.length} 次筹码记录）。地图专项规则仍在持续校准，部分极端卡牌交互以原生对局为准。
            </div>
          </div>

          <nav className="replay-tabs no-print" aria-label="复盘分析视图切换">
            {(
              [
                ['overview', '数据总览'],
                ['board', '逐轮回放'],
                ['chips', '筹码选择'],
                ['report', '完整报告'],
                ['export', '导出战报'],
              ] as Array<[ReplayTabKey, string]>
            ).map(([key, label]) => (
              <button
                key={key}
                type="button"
                className={`replay-tab${activeTab === key ? ' active' : ''}`}
                onClick={() => setActiveTab(key)}
              >
                {label}
              </button>
            ))}
          </nav>

          {activeTab === 'overview' && (
            <ReplayOverviewTabs
              report={report}
              onSwitchTab={setActiveTab}
              onJumpToStep={jumpToStep}
            />
          )}

          {activeTab === 'board' && (
            <ReplayBoardView
              report={report}
              stepIndex={boardStepIndex}
              onSelectStep={setBoardStepIndex}
            />
          )}

          {activeTab === 'chips' && (
            <ReplayChipsTab report={report} onJumpToStep={jumpToStep} />
          )}

          {activeTab === 'report' && <ReplayFullReportTab report={report} />}

          {activeTab === 'export' && <ReplayExportsTab report={report} />}
        </>
      )}
    </div>
  );
};
