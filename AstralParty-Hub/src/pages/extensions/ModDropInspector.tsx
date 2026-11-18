import { useCallback, useEffect, useRef, useState, type ChangeEvent } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  FileArchive,
  FileText,
  FolderUp,
  Image as ImageIcon,
  RotateCcw,
  ShieldCheck,
  Sparkles,
  Upload,
  X,
} from 'lucide-react';
import {
  api,
  type DroppedModInspectResult,
  type DroppedModInstallResult,
  type DroppedModRecord,
} from '../../shared/api/tauri';
import { formatModBytes } from '../../shared/workshopClient';
import { usePendingDroppedModPaths } from '../../shared/modDropBridge';

interface Props {
  installationId: string | null;
  onBusy: (busy: boolean) => void;
  disabled?: boolean;
  onInstalled: () => void;
}

const SOURCE_KIND_LABELS: Record<string, string> = {
  community_zip: '社区模组 ZIP',
  xspkg: '.xspkg 扩展包',
  apmm: '.apmm 模组包',
  single_bundle: '单 Bundle 文件',
  folder: '本地模组文件夹',
};

const TARGET_KIND_LABELS: Record<string, string> = {
  streaming_assets: 'StreamingAssets/aa',
  addressables: 'Addressables/__data',
  catalog: 'catalog.bundle',
  xspkg: '.xspkg 清单目标',
};

const errorText = (err: unknown) =>
  err && typeof err === 'object' && 'message' in err ? String((err as { message: unknown }).message) : String(err);

export function ModDropInspector({ installationId, onBusy, disabled = false, onInstalled }: Props) {
  const [pendingPaths, clearPendingPaths] = usePendingDroppedModPaths();
  const [sourcePath, setSourcePath] = useState('');
  const [queuedPaths, setQueuedPaths] = useState<string[]>([]);
  const [inspectResult, setInspectResult] = useState<DroppedModInspectResult | null>(null);
  const [installResult, setInstallResult] = useState<DroppedModInstallResult | null>(null);
  const [records, setRecords] = useState<DroppedModRecord[]>([]);
  const [includeCatalog, setIncludeCatalog] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [feedback, setFeedback] = useState('');
  const fileInputRef = useRef<HTMLInputElement>(null);
  const sectionRef = useRef<HTMLElement>(null);

  const loadRecords = useCallback(async () => {
    if (!installationId) {
      setRecords([]);
      return;
    }
    try {
      setRecords(await api.listDroppedMods(installationId));
    } catch {
      // Non-fatal
    }
  }, [installationId]);

  useEffect(() => {
    void loadRecords();
  }, [loadRecords]);

  const inspectPath = useCallback(
    async (pathToInspect: string) => {
      const clean = pathToInspect.trim().replace(/^"|"$/g, '');
      if (!clean) return;
      setSourcePath(clean);
      setInstallResult(null);
      setFeedback('');
      setError('');

      if (!installationId) {
        setInspectResult(null);
        setError('已接收拖入文件，请先在上方选择并定位游戏目录，再点击「重新检测」。');
        return;
      }

      setBusy(true);
      onBusy(true);
      try {
        const res = await api.inspectDroppedMod(installationId, clean);
        setInspectResult(res);
        setIncludeCatalog(false);
      } catch (e) {
        setInspectResult(null);
        setError(errorText(e));
      } finally {
        setBusy(false);
        onBusy(false);
      }
    },
    [installationId, onBusy],
  );

  // Consume paths dropped anywhere on the Hub window.
  useEffect(() => {
    if (!pendingPaths.length) return;
    setQueuedPaths(pendingPaths);
    const first = pendingPaths[0];
    clearPendingPaths();
    void inspectPath(first);
    sectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, [pendingPaths, clearPendingPaths, inspectPath]);

  // Re-inspect automatically if user dropped a file before selecting an installation.
  useEffect(() => {
    if (installationId && sourcePath && !inspectResult && !busy && error.includes('先在上方选择')) {
      void inspectPath(sourcePath);
    }
  }, [installationId, sourcePath, inspectResult, busy, error, inspectPath]);

  const handleFilePick = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    setBusy(true);
    onBusy(true);
    setError('');
    setFeedback('');
    setInstallResult(null);
    try {
      const buffer = await file.arrayBuffer();
      const bytes = Array.from(new Uint8Array(buffer));
      const stagedPath = await api.stageDroppedModBytes(file.name, bytes);
      setQueuedPaths([stagedPath]);
      await inspectPath(stagedPath);
    } catch (err) {
      setError(errorText(err));
      setBusy(false);
      onBusy(false);
    }
  };

  const handleInstall = async () => {
    if (!installationId || !inspectResult || busy || disabled) return;
    setBusy(true);
    onBusy(true);
    setError('');
    setFeedback('');
    try {
      const res = await api.installDroppedMod(installationId, inspectResult.sourcePath, includeCatalog);
      setInstallResult(res);
      setFeedback(res.message);
      await loadRecords();
      const refreshed = await api.inspectDroppedMod(installationId, inspectResult.sourcePath);
      setInspectResult(refreshed);
      onInstalled();
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
      onBusy(false);
    }
  };

  const handleRestore = async (record: DroppedModRecord) => {
    if (!installationId || busy || disabled) return;
    if (!confirm(`确定还原「${record.name}」安装前的原版文件？备份将保留在本地目录中。`)) return;
    setBusy(true);
    onBusy(true);
    setError('');
    setFeedback('');
    try {
      const msg = await api.restoreDroppedMod(installationId, record.modId);
      setFeedback(msg);
      await loadRecords();
      if (inspectResult && inspectResult.modId === record.modId) {
        const refreshed = await api.inspectDroppedMod(installationId, inspectResult.sourcePath);
        setInspectResult(refreshed);
      }
      onInstalled();
    } catch (e) {
      setError(errorText(e));
    } finally {
      setBusy(false);
      onBusy(false);
    }
  };

  const nonCatalogCount = inspectResult ? inspectResult.matchedFiles.filter((f) => !f.isCatalog).length : 0;
  const installableCount = inspectResult
    ? inspectResult.matchedFiles.filter((f) => (includeCatalog || !f.isCatalog) && !f.conflictWith).length
    : 0;
  const canExecuteInstall =
    Boolean(inspectResult?.canInstall) &&
    Boolean(installationId) &&
    installableCount > 0 &&
    !busy &&
    !disabled;

  return (
    <section className="panel mod-drop-section" ref={sectionRef} aria-label="本地模组拖入检测与安装">
      <div className="mod-drop-header">
        <div>
          <h2>
            <FolderUp size={18} />
            <span>拖入本地 Mod 检测与安装</span>
          </h2>
          <p>
            将下载的模组 <code>.zip</code>、<code>.xspkg</code>、<code>.apmm</code>、<code>.bundle</code> 或解压文件夹直接拖入窗口任意位置，自动匹配游戏目录并备份原版文件后安装。
          </p>
        </div>
        <div className="mod-drop-actions">
          <input
            ref={fileInputRef}
            type="file"
            accept=".zip,.xspkg,.apmm,.bundle"
            style={{ display: 'none' }}
            onChange={(e) => void handleFilePick(e)}
          />
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            disabled={busy || disabled}
            onClick={() => fileInputRef.current?.click()}
          >
            <Upload size={14} />
            选择模组文件
          </button>
        </div>
      </div>

      <form
        className="mod-drop-path-form"
        onSubmit={(e) => {
          e.preventDefault();
          void inspectPath(sourcePath);
        }}
      >
        <input
          type="text"
          className="form-input"
          value={sourcePath}
          disabled={busy || disabled}
          onChange={(e) => setSourcePath(e.target.value)}
          placeholder="直接拖入模组包/文件夹，或粘贴本地 .zip / .xspkg / .apmm / .bundle / 文件夹路径…"
          aria-label="本地模组路径"
        />
        <button type="submit" className="btn btn-primary btn-sm" disabled={busy || disabled || !sourcePath.trim()}>
          {busy ? '检测中…' : '检测文件'}
        </button>
      </form>

      {queuedPaths.length > 1 && (
        <div className="mod-drop-queue">
          <span>已拖入 {queuedPaths.length} 个项目：</span>
          {queuedPaths.map((p) => {
            const label = p.split(/[\\/]/).filter(Boolean).pop() || p;
            return (
              <button
                key={p}
                type="button"
                className={`btn btn-sm ${p === sourcePath ? 'btn-primary' : 'btn-secondary'}`}
                onClick={() => void inspectPath(p)}
              >
                {label}
              </button>
            );
          })}
        </div>
      )}

      {error && (
        <div className="workshop-notice error" role="alert">
          <AlertTriangle size={16} />
          <span>{error}</span>
          <button type="button" className="workshop-notice-close" onClick={() => setError('')} aria-label="关闭错误">
            <X size={14} />
          </button>
        </div>
      )}

      {feedback && (
        <div className="workshop-notice success" role="status">
          <CheckCircle2 size={16} />
          <span>{feedback}</span>
          <button type="button" className="workshop-notice-close" onClick={() => setFeedback('')} aria-label="关闭提示">
            <X size={14} />
          </button>
        </div>
      )}

      {inspectResult && (
        <div className="mod-drop-preview">
          <div className="mod-drop-preview-head">
            <div>
              <div className="mod-drop-title-row">
                <FileArchive size={17} />
                <strong>{inspectResult.displayName}</strong>
                <span className="workshop-kind-chip">
                  {SOURCE_KIND_LABELS[inspectResult.sourceKind] || inspectResult.sourceKind}
                </span>
                <span className="mod-drop-chip-muted">{inspectResult.gamePlatform}</span>
              </div>
              <code className="extension-path">{inspectResult.sourcePath}</code>
            </div>

            <div className="mod-drop-preview-cta">
              {inspectResult.alreadyInstalled ? (
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  disabled={busy || disabled}
                  onClick={() => {
                    const existing = records.find((r) => r.modId === inspectResult.modId);
                    if (existing) void handleRestore(existing);
                  }}
                >
                  <RotateCcw size={14} />
                  已安装 · 一键还原
                </button>
              ) : (
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  disabled={!canExecuteInstall}
                  onClick={() => void handleInstall()}
                >
                  <ShieldCheck size={14} />
                  {busy ? '正在安装…' : `备份并安装（${installableCount} 个）`}
                </button>
              )}
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                disabled={busy}
                onClick={() => {
                  setInspectResult(null);
                  setInstallResult(null);
                  setSourcePath('');
                  setQueuedPaths([]);
                }}
              >
                清除
              </button>
            </div>
          </div>

          {inspectResult.warnings.length > 0 && (
            <div className="mod-drop-warnings">
              {inspectResult.warnings.map((w) => (
                <p key={w}>
                  <AlertTriangle size={14} />
                  <span>{w}</span>
                </p>
              ))}
            </div>
          )}

          {inspectResult.hasCatalogBundle && (
            <label className="mod-drop-catalog-toggle">
              <input
                type="checkbox"
                checked={includeCatalog}
                disabled={busy || disabled || inspectResult.alreadyInstalled}
                onChange={(e) => setIncludeCatalog(e.target.checked)}
              />
              <span>
                同时替换 <code>catalog.bundle</code>（高风险：游戏版本不一致可能导致白屏或闪退{nonCatalogCount === 0 ? '；此包仅含 catalog.bundle，需勾选后才能安装' : ''}）
              </span>
            </label>
          )}

          {inspectResult.conflicts.length > 0 && (
            <div className="mod-drop-conflicts">
              <strong>检测到文件占用 / 安全冲突：</strong>
              <ul>
                {inspectResult.conflicts.map((c) => (
                  <li key={c}>{c}</li>
                ))}
              </ul>
            </div>
          )}

          <div className="mod-drop-stats-row">
            <span>匹配目标：<strong>{inspectResult.matchedFiles.length}</strong></span>
            <span>未匹配跳过：<strong>{inspectResult.unmatchedFiles.length}</strong></span>
            <span>预览图/视频：<strong>{inspectResult.previewFiles.length}</strong></span>
            <span>说明文档：<strong>{inspectResult.docFiles.length}</strong></span>
          </div>

          {inspectResult.matchedFiles.length > 0 && (
            <div className="mod-drop-file-list">
              {inspectResult.matchedFiles.map((f) => (
                <div
                  key={`${f.sourceEntry}->${f.targetRel}`}
                  className={`mod-drop-file-row ${f.isCatalog ? 'is-risky' : ''} ${f.conflictWith ? 'is-conflict' : ''}`}
                >
                  <div className="mod-drop-file-main">
                    <span className={`mod-drop-badge ${f.isCatalog ? 'warn' : 'ok'}`}>
                      {TARGET_KIND_LABELS[f.targetKind] || f.targetKind}
                    </span>
                    <code>{f.targetRel}</code>
                    <span className="mod-drop-file-size">{formatModBytes(f.size)}</span>
                  </div>
                  <div className="mod-drop-file-sub">
                    <span>来源：{f.sourceEntry}</span>
                    {f.alreadySame && <span className="mod-drop-same">内容与当前磁盘文件一致</span>}
                    {f.conflictWith && <span className="mod-conflict">冲突：{f.conflictWith}</span>}
                  </div>
                </div>
              ))}
            </div>
          )}

          {inspectResult.unmatchedFiles.length > 0 && (
            <details className="mod-drop-extra">
              <summary>查看未匹配并自动跳过的 Bundle（{inspectResult.unmatchedFiles.length}）</summary>
              <div className="mod-drop-file-list">
                {inspectResult.unmatchedFiles.map((u) => (
                  <div key={u.sourceEntry} className="mod-drop-file-row">
                    <div className="mod-drop-file-main">
                      <code>{u.sourceEntry}</code>
                      <span className="mod-drop-file-size">{formatModBytes(u.size)}</span>
                    </div>
                    <div className="mod-drop-file-sub">
                      <span>{u.reason}</span>
                    </div>
                  </div>
                ))}
              </div>
            </details>
          )}

          {(inspectResult.previewFiles.length > 0 || inspectResult.docFiles.length > 0) && (
            <div className="mod-drop-media-summary">
              {inspectResult.previewFiles.length > 0 && (
                <span>
                  <ImageIcon size={13} /> 附带预览：{inspectResult.previewFiles.join('、')}（仅展示，不写入游戏目录）
                </span>
              )}
              {inspectResult.docFiles.length > 0 && (
                <span>
                  <FileText size={13} /> 附带说明：{inspectResult.docFiles.join('、')}
                </span>
              )}
            </div>
          )}

          {installResult && (
            <div className="mod-drop-install-ok">
              <Sparkles size={15} />
              <span>
                {installResult.message}
                {installResult.backupId ? `（备份 ID：${installResult.backupId.slice(0, 8)}）` : ''}
              </span>
            </div>
          )}
        </div>
      )}

      {records.filter((r) => r.canRestore).length > 0 && (
        <div className="mod-drop-installed-list">
          <h3>已安装的本地拖入模组（{records.filter((r) => r.canRestore).length}）</h3>
          <div className="mod-drop-record-grid">
            {records
              .filter((r) => r.canRestore)
              .map((rec) => (
                <div key={rec.modId} className="mod-drop-record-card">
                  <div>
                    <strong>{rec.name}</strong>
                    <span>
                      {SOURCE_KIND_LABELS[rec.sourceKind] || rec.sourceKind} · {rec.fileCount} 个文件
                      {rec.hasCatalog ? ' · 含 catalog' : ''}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    disabled={busy || disabled}
                    onClick={() => void handleRestore(rec)}
                  >
                    <RotateCcw size={13} />
                    一键还原
                  </button>
                </div>
              ))}
          </div>
        </div>
      )}
    </section>
  );
}
