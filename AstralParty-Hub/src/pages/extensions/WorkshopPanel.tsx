import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import {
  CheckCircle2,
  Download,
  ExternalLink,
  Image as ImageIcon,
  Info,
  Layers,
  Package,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  ShieldCheck,
  Sparkles,
  Upload,
  Volume2,
  X,
} from 'lucide-react';
import { api, type WorkshopLocalItemState } from '../../shared/api/tauri';
import { isTauriRuntime, openExternalUrl, webApiBaseUrl } from '../../shared/runtime';
import type { GameInstallation } from '../../shared/types';
import {
  WORKSHOP_CATEGORIES,
  fetchWorkshopMods,
  formatModBytes,
  installKindLabel,
  submitWorkshopMod,
  workshopAssetUrl,
  type WorkshopCategory,
  type WorkshopModItem,
  type WorkshopSubmitResponse,
} from '../../shared/workshopClient';

export interface WorkshopPanelProps {
  installationId?: string | null;
  installation?: GameInstallation | null;
  onBusy?: (busy: boolean) => void;
  disabled?: boolean;
  onInstalled?: () => void;
}

function CategoryGlyph({ category }: { category: string }) {
  switch (category) {
    case 'card':
      return <Layers size={26} />;
    case 'optimization':
      return <Sparkles size={26} />;
    case 'voice_vfx':
      return <Volume2 size={26} />;
    default:
      return <ImageIcon size={26} />;
  }
}

export function WorkshopPanel({
  installationId,
  installation,
  onBusy,
  disabled,
  onInstalled,
}: WorkshopPanelProps) {
  const isDesktop = isTauriRuntime();
  const [category, setCategory] = useState<WorkshopCategory>('all');
  const [query, setQuery] = useState('');
  const [items, setItems] = useState<WorkshopModItem[]>([]);
  const [total, setTotal] = useState(0);
  const [counts, setCounts] = useState<Record<string, number>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [localStates, setLocalStates] = useState<Record<string, WorkshopLocalItemState>>({});
  const [actionId, setActionId] = useState('');
  const [notice, setNotice] = useState<{ kind: 'success' | 'error' | 'info'; text: string } | null>(null);

  const [detailItem, setDetailItem] = useState<WorkshopModItem | null>(null);
  const [failedPreviews, setFailedPreviews] = useState<Record<string, boolean>>({});
  const [submitOpen, setSubmitOpen] = useState(false);

  // Submission form state
  const [subName, setSubName] = useState('');
  const [subAuthor, setSubAuthor] = useState('');
  const [subCategory, setSubCategory] = useState<Exclude<WorkshopCategory, 'all'>>('skin');
  const [subTags, setSubTags] = useState('');
  const [subTargetInfo, setSubTargetInfo] = useState('');
  const [subDescription, setSubDescription] = useState('');
  const [subExternalUrl, setSubExternalUrl] = useState('');
  const [subArchive, setSubArchive] = useState<File | null>(null);
  const [subPreview, setSubPreview] = useState<File | null>(null);
  const [subSubmitting, setSubSubmitting] = useState(false);
  const [subError, setSubError] = useState('');
  const [subResult, setSubResult] = useState<WorkshopSubmitResponse | null>(null);

  const loadLocalStates = useCallback(async () => {
    if (!isDesktop || !installationId) {
      setLocalStates({});
      return;
    }
    try {
      const list = await api.workshopListLocalState(installationId);
      const map: Record<string, WorkshopLocalItemState> = {};
      for (const entry of list) {
        map[entry.modId] = entry;
      }
      setLocalStates(map);
    } catch {
      // Non-fatal: local receipt scan may be unavailable before game dir selection
    }
  }, [installationId, isDesktop]);

  const loadMods = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const data = await fetchWorkshopMods(category, query);
      setItems(data.items || []);
      setTotal(data.total || 0);
      setCounts(data.counts || {});
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, [category, query]);

  useEffect(() => {
    void loadMods();
  }, [loadMods]);

  useEffect(() => {
    void loadLocalStates();
  }, [loadLocalStates]);

  useEffect(() => {
    const onEsc = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setDetailItem(null);
        if (!subSubmitting) setSubmitOpen(false);
      }
    };
    window.addEventListener('keydown', onEsc);
    return () => window.removeEventListener('keydown', onEsc);
  }, [subSubmitting]);

  const handleDesktopInstall = async (item: WorkshopModItem) => {
    if (!installationId) {
      setNotice({ kind: 'error', text: '请先在上方选择并定位游戏目录，再一键安装模组。' });
      return;
    }
    setActionId(item.id);
    onBusy?.(true);
    setNotice({ kind: 'info', text: `正在下载并校验「${item.name}」…` });
    try {
      const result = await api.workshopDownloadAndInstall({
        installationId,
        apiBase: webApiBaseUrl(),
        modId: item.id,
        name: item.name,
        installKind: item.installKind,
        gameVersion: item.gameVersion,
        platforms: item.platforms,
        downloadUrl: item.downloadUrl,
        sha256: item.sha256,
        sizeBytes: item.sizeBytes,
        files: (item.files || []).map((f) => ({
          target: f.target,
          sha256: f.sha256,
          originalSha256: f.originalSha256,
          size: f.size,
          payloadUrl: f.payloadUrl,
        })),
      });
      setNotice({ kind: 'success', text: result.message });
      await loadLocalStates();
      onInstalled?.();
    } catch (err) {
      setNotice({ kind: 'error', text: err instanceof Error ? err.message : String(err) });
    } finally {
      setActionId('');
      onBusy?.(false);
    }
  };

  const handleDesktopRestore = async (item: WorkshopModItem) => {
    if (!installationId) return;
    setActionId(item.id);
    onBusy?.(true);
    try {
      const msg = await api.workshopRestoreMod(installationId, item.id);
      setNotice({ kind: 'success', text: msg });
      await loadLocalStates();
      onInstalled?.();
    } catch (err) {
      setNotice({ kind: 'error', text: err instanceof Error ? err.message : String(err) });
    } finally {
      setActionId('');
      onBusy?.(false);
    }
  };

  const resetSubmissionForm = () => {
    setSubName('');
    setSubAuthor('');
    setSubCategory('skin');
    setSubTags('');
    setSubTargetInfo('');
    setSubDescription('');
    setSubExternalUrl('');
    setSubArchive(null);
    setSubPreview(null);
    setSubError('');
  };

  const handleSubmitMod = async (e: FormEvent) => {
    e.preventDefault();
    setSubError('');
    setSubResult(null);
    setSubSubmitting(true);
    try {
      const res = await submitWorkshopMod({
        name: subName,
        author: subAuthor,
        category: subCategory,
        tags: subTags,
        targetInfo: subTargetInfo,
        description: subDescription,
        externalUrl: subExternalUrl,
        archiveFile: subArchive,
        previewFile: subPreview,
      });
      setSubResult(res);
      resetSubmissionForm();
      void loadMods();
    } catch (err) {
      setSubError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubSubmitting(false);
    }
  };

  const targetSummary = useMemo(() => {
    if (!isDesktop) {
      return '网页端支持按分类浏览、预览与下载模组包；使用桌面端星枢 Hub 可开启 SHA-256 核验、原子备份与一键安装/还原。';
    }
    if (!installation) {
      return '请先在上方选择或添加「星引擎对决」安装目录，然后点击任意模组的「下载并安装」即可自动备份并写入。';
    }
    return `当前安装目标：${installation.platform} · ${installation.install_path}（写入前会校验游戏关闭状态并自动保存原版备份）`;
  }, [installation, isDesktop]);

  return (
    <section className="workshop-section" aria-label="模组工坊">
      <div className="panel workshop-hero">
        <div className="workshop-hero-copy">
          <div className="workshop-eyebrow">
            <Sparkles size={15} />
            <span>MOD WORKSHOP · 优化 Mod / 卡牌替换 / 角色皮肤 / 语音特效</span>
          </div>
          <h2>星引擎游戏优化与资源替换工坊</h2>
          <p>{targetSummary}</p>
        </div>
        <div className="workshop-hero-actions">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => {
              void loadMods();
              void loadLocalStates();
            }}
            disabled={loading || disabled}
          >
            <RefreshCw size={14} />
            刷新列表
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => {
              setSubResult(null);
              setSubError('');
              setSubmitOpen(true);
            }}
            disabled={disabled}
          >
            <Plus size={15} />
            添加投稿
          </button>
        </div>
      </div>

      <div className="panel workshop-controls">
        <div className="workshop-categories" role="tablist" aria-label="模组分类">
          {WORKSHOP_CATEGORIES.map((cat) => {
            const count = cat.id === 'all' ? total : (counts[cat.id] ?? 0);
            const active = category === cat.id;
            return (
              <button
                key={cat.id}
                type="button"
                role="tab"
                aria-selected={active}
                className={`workshop-pill ${active ? 'active' : ''}`}
                onClick={() => setCategory(cat.id)}
              >
                <span>{cat.labelZh}</span>
                <span className="workshop-pill-count">{count}</span>
              </button>
            );
          })}
        </div>
        <label className="workshop-search">
          <Search size={15} />
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="搜索模组名、角色、作者、标签或 Bundle 资源名…"
            aria-label="搜索工坊模组"
          />
        </label>
      </div>

      {notice && (
        <div className={`workshop-notice ${notice.kind}`} role="status">
          <Info size={16} />
          <span>{notice.text}</span>
          <button type="button" className="workshop-notice-close" onClick={() => setNotice(null)} aria-label="关闭提示">
            <X size={14} />
          </button>
        </div>
      )}

      {error && (
        <div className="workshop-notice error" role="alert">
          <Info size={16} />
          <span>{error}</span>
        </div>
      )}

      {loading ? (
        <div className="panel workshop-empty">
          <RefreshCw size={24} className="spin" />
          <p>正在加载工坊模组目录…</p>
        </div>
      ) : items.length === 0 ? (
        <div className="panel workshop-empty">
          <Package size={28} />
          <h3>当前筛选下暂无工坊条目</h3>
          <p>你可以切换分类或搜索关键词，或点击右上角「添加投稿」分享自己的 Mod / 卡面 / 皮肤包。</p>
        </div>
      ) : (
        <div className="workshop-grid">
          {items.map((item) => {
            const local = localStates[item.id];
            const isBusy = actionId === item.id || Boolean(disabled);
            const previewSrc = item.previewUrl && !failedPreviews[item.id] ? workshopAssetUrl(item.previewUrl) : '';
            const canDirectInstall =
              isDesktop &&
              Boolean(installationId) &&
              (item.files.length > 0 || (item.downloadable && Boolean(item.downloadUrl?.startsWith('/api/'))));
            const webApiDownload = item.downloadable && item.downloadUrl?.startsWith('/api/');

            return (
              <article key={item.id} className="panel workshop-card">
                <div className="workshop-preview">
                  {previewSrc ? (
                    <img
                      src={previewSrc}
                      alt={item.name}
                      className="workshop-preview-img"
                      loading="lazy"
                      onError={() => setFailedPreviews((prev) => ({ ...prev, [item.id]: true }))}
                    />
                  ) : (
                    <div className={`workshop-preview-fallback cat-${item.category}`}>
                      <CategoryGlyph category={item.category} />
                      <span className="workshop-preview-cat">{item.categoryLabel}</span>
                      <code className="workshop-preview-asset">
                        {item.assets?.[0] || item.targetInfo || `${item.files.length} 个资源文件`}
                      </code>
                    </div>
                  )}
                  <div className="workshop-preview-badges">
                    <span className={`workshop-badge cat-${item.category}`}>{item.categoryLabel}</span>
                    {local?.canRestore && (
                      <span className="workshop-badge installed">
                        <CheckCircle2 size={12} /> 已安装
                      </span>
                    )}
                    {!local?.canRestore && local?.phase === 'downloaded' && (
                      <span className="workshop-badge downloaded">已下载</span>
                    )}
                  </div>
                </div>

                <div className="workshop-card-body">
                  <div className="workshop-card-head">
                    <h3 title={item.name}>{item.name}</h3>
                    <span className="workshop-kind-chip">{installKindLabel(item.installKind)}</span>
                  </div>

                  <div className="workshop-meta">
                    <span>作者：{item.author}</span>
                    <span>大小：{formatModBytes(item.sizeBytes)}</span>
                    <span>
                      {(item.platforms || ['STEAM_CN']).join('/')} · v{item.gameVersion || '3.3.0'}
                    </span>
                  </div>

                  <p className="workshop-desc">{item.description}</p>

                  {item.tags.length > 0 && (
                    <div className="workshop-tags">
                      {item.tags.slice(0, 4).map((tag) => (
                        <span key={tag}>{tag}</span>
                      ))}
                    </div>
                  )}

                  {item.targetInfo && (
                    <code className="workshop-target" title={item.targetInfo}>
                      目标：{item.targetInfo}
                    </code>
                  )}
                </div>

                <footer className="workshop-card-footer">
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={() => setDetailItem(item)}
                  >
                    <Info size={14} />
                    详情
                  </button>

                  {isDesktop ? (
                    <div className="workshop-footer-actions">
                      {local?.canRestore ? (
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          disabled={isBusy}
                          onClick={() => void handleDesktopRestore(item)}
                        >
                          <RotateCcw size={14} />
                          {actionId === item.id ? '还原中…' : '一键还原'}
                        </button>
                      ) : canDirectInstall ? (
                        <button
                          type="button"
                          className="btn btn-primary btn-sm"
                          disabled={isBusy}
                          onClick={() => void handleDesktopInstall(item)}
                        >
                          <ShieldCheck size={14} />
                          {actionId === item.id ? '安装中…' : '下载并安装'}
                        </button>
                      ) : item.externalUrl ? (
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => void openExternalUrl(item.externalUrl!)}
                        >
                          <ExternalLink size={14} />
                          发布页下载
                        </button>
                      ) : (
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => setDetailItem(item)}
                        >
                          查看说明
                        </button>
                      )}
                    </div>
                  ) : (
                    <div className="workshop-footer-actions">
                      {webApiDownload && item.downloadUrl ? (
                        <a
                          className="btn btn-primary btn-sm"
                          href={workshopAssetUrl(item.downloadUrl)}
                          download
                        >
                          <Download size={14} />
                          下载模组包
                        </a>
                      ) : item.externalUrl ? (
                        <button
                          type="button"
                          className="btn btn-primary btn-sm"
                          onClick={() => void openExternalUrl(item.externalUrl!)}
                        >
                          <ExternalLink size={14} />
                          外链下载
                        </button>
                      ) : (
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          title="该预置条目资产未在网页端整包托管，可在桌面端星枢 Hub 启用本地模组库"
                          onClick={() => setDetailItem(item)}
                        >
                          <Package size={14} />
                          桌面内置库
                        </button>
                      )}
                    </div>
                  )}
                </footer>
              </article>
            );
          })}
        </div>
      )}

      {detailItem && (
        <div
          className="workshop-modal-backdrop"
          role="presentation"
          onClick={() => setDetailItem(null)}
        >
          <div
            className="workshop-modal"
            role="dialog"
            aria-modal="true"
            aria-label={detailItem.name}
            onClick={(e) => e.stopPropagation()}
          >
            <header className="workshop-modal-header">
              <div>
                <span className={`workshop-badge cat-${detailItem.category}`}>{detailItem.categoryLabel}</span>
                <h3>{detailItem.name}</h3>
              </div>
              <button type="button" className="workshop-modal-close" onClick={() => setDetailItem(null)} aria-label="关闭弹窗">
                <X size={18} />
              </button>
            </header>

            <div className="workshop-modal-body">
              {detailItem.previewUrl && !failedPreviews[detailItem.id] && (
                <img
                  src={workshopAssetUrl(detailItem.previewUrl)}
                  alt={detailItem.name}
                  className="workshop-detail-preview"
                />
              )}

              <div className="workshop-detail-meta">
                <div><span>作者</span><strong>{detailItem.author}</strong></div>
                <div><span>打包类型</span><strong>{installKindLabel(detailItem.installKind)}</strong></div>
                <div><span>体积</span><strong>{formatModBytes(detailItem.sizeBytes)}</strong></div>
                <div><span>适配版本</span><strong>{(detailItem.platforms || ['STEAM_CN']).join('/')} v{detailItem.gameVersion}</strong></div>
              </div>

              <p className="workshop-detail-desc">{detailItem.description}</p>

              {detailItem.sha256 && (
                <div className="workshop-detail-block">
                  <span>SHA-256 完整性摘要</span>
                  <code>{detailItem.sha256}</code>
                </div>
              )}

              {detailItem.targetInfo && (
                <div className="workshop-detail-block">
                  <span>替换目标摘要</span>
                  <code>{detailItem.targetInfo}</code>
                </div>
              )}

              {detailItem.files.length > 0 && (
                <div className="workshop-detail-block">
                  <span>资产映射清单（{detailItem.files.length} 个文件）</span>
                  <div className="workshop-file-list">
                    {detailItem.files.map((f, idx) => (
                      <div key={`${f.target}-${idx}`} className="workshop-file-row">
                        <code>{f.target}</code>
                        <span>{(f.assets || []).join(', ') || formatModBytes(f.size)}</span>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {!detailItem.downloadable && detailItem.isSeed && (
                <p className="workshop-detail-note">
                  说明：该条目来自星枢预置资源清单；若服务端未上传对应 Bundle 整包文件，请在下方「本地模组库」或使用星枢 Hub 桌面发布包配套的 ModLibrary 安装。
                </p>
              )}
            </div>

            <footer className="workshop-modal-footer">
              <button type="button" className="btn btn-secondary" onClick={() => setDetailItem(null)}>
                关闭
              </button>
              {detailItem.downloadable && detailItem.downloadUrl?.startsWith('/api/') && (
                <a className="btn btn-secondary" href={workshopAssetUrl(detailItem.downloadUrl)} download>
                  <Download size={14} /> 下载文件包
                </a>
              )}
              {detailItem.externalUrl && (
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => void openExternalUrl(detailItem.externalUrl!)}
                >
                  <ExternalLink size={14} /> 打开外部发布页
                </button>
              )}
              {isDesktop && installationId && (
                localStates[detailItem.id]?.canRestore ? (
                  <button
                    type="button"
                    className="btn btn-secondary"
                    disabled={Boolean(actionId)}
                    onClick={() => void handleDesktopRestore(detailItem)}
                  >
                    <RotateCcw size={14} /> 一键还原
                  </button>
                ) : (detailItem.files.length > 0 || detailItem.downloadable) ? (
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={Boolean(actionId)}
                    onClick={() => void handleDesktopInstall(detailItem)}
                  >
                    <ShieldCheck size={14} /> 下载并安装到游戏
                  </button>
                ) : null
              )}
            </footer>
          </div>
        </div>
      )}

      {submitOpen && (
        <div
          className="workshop-modal-backdrop"
          role="presentation"
          onClick={() => { if (!subSubmitting) setSubmitOpen(false); }}
        >
          <div
            className="workshop-modal"
            role="dialog"
            aria-modal="true"
            aria-label="添加模组投稿"
            onClick={(e) => e.stopPropagation()}
          >
            <header className="workshop-modal-header">
              <div>
                <span className="workshop-badge">社区投稿</span>
                <h3>提交 Mod / 卡牌 / 皮肤到工坊</h3>
              </div>
              <button
                type="button"
                className="workshop-modal-close"
                onClick={() => { if (!subSubmitting) setSubmitOpen(false); }}
                aria-label="关闭弹窗"
              >
                <X size={18} />
              </button>
            </header>

            <form onSubmit={handleSubmitMod} className="workshop-modal-body workshop-form">
              {subResult ? (
                <div className="workshop-submit-success">
                  <CheckCircle2 size={22} />
                  <div>
                    <strong>{subResult.message}</strong>
                    <p>投稿编号：{subResult.id} · 识别类型：{installKindLabel(subResult.installKind)}</p>
                    {subResult.sha256 && <p>SHA-256：<code>{subResult.sha256}</code></p>}
                  </div>
                </div>
              ) : (
                <>
                  <div className="workshop-form-row">
                    <label>
                      <span>模组名称 *</span>
                      <input
                        type="text"
                        required
                        minLength={2}
                        maxLength={80}
                        value={subName}
                        onChange={(e) => setSubName(e.target.value)}
                        placeholder="例如：芙兰卡高冷立绘替换 / 优化特效卡面"
                      />
                    </label>
                    <label>
                      <span>作者署名 *</span>
                      <input
                        type="text"
                        required
                        minLength={1}
                        maxLength={40}
                        value={subAuthor}
                        onChange={(e) => setSubAuthor(e.target.value)}
                        placeholder="你的昵称或署名"
                      />
                    </label>
                  </div>

                  <div className="workshop-form-row">
                    <label>
                      <span>模组分类 *</span>
                      <select
                        value={subCategory}
                        onChange={(e) => setSubCategory(e.target.value as Exclude<WorkshopCategory, 'all'>)}
                      >
                        <option value="optimization">优化 Mod</option>
                        <option value="card">卡牌替换</option>
                        <option value="skin">角色皮肤</option>
                        <option value="voice_vfx">语音·特效</option>
                      </select>
                    </label>
                    <label>
                      <span>标签（逗号分隔，最多 8 个）</span>
                      <input
                        type="text"
                        value={subTags}
                        onChange={(e) => setSubTags(e.target.value)}
                        placeholder="角色皮肤, 莉莉, 轻量化"
                      />
                    </label>
                  </div>

                  <label>
                    <span>替换目标说明（角色、卡牌或 Bundle 名）</span>
                    <input
                      type="text"
                      maxLength={160}
                      value={subTargetInfo}
                      onChange={(e) => setSubTargetInfo(e.target.value)}
                      placeholder="例如：UT_Hero_Bust_103 / 治疗卡卡面优化"
                    />
                  </label>

                  <label>
                    <span>模组说明 *</span>
                    <textarea
                      rows={3}
                      required
                      minLength={4}
                      maxLength={600}
                      value={subDescription}
                      onChange={(e) => setSubDescription(e.target.value)}
                      placeholder="说明具体修改内容、适配版本（如 3.3.0）及注意事项…"
                    />
                  </label>

                  <div className="workshop-form-row">
                    <label className="workshop-file-input">
                      <span>上传模组压缩包（.zip / .xspkg，≤ 24MB）</span>
                      <div className="workshop-file-picker">
                        <Upload size={15} />
                        <span>{subArchive ? `${subArchive.name} (${formatModBytes(subArchive.size)})` : '选择本地 .zip 或 .xspkg'}</span>
                        <input
                          type="file"
                          accept=".zip,.xspkg"
                          onChange={(e) => setSubArchive(e.target.files?.[0] || null)}
                        />
                      </div>
                    </label>
                    <label className="workshop-file-input">
                      <span>可选预览图（PNG / JPG / WebP，≤ 2MB）</span>
                      <div className="workshop-file-picker">
                        <ImageIcon size={15} />
                        <span>{subPreview ? `${subPreview.name} (${formatModBytes(subPreview.size)})` : '选择效果截图'}</span>
                        <input
                          type="file"
                          accept="image/png,image/jpeg,image/webp"
                          onChange={(e) => setSubPreview(e.target.files?.[0] || null)}
                        />
                      </div>
                    </label>
                  </div>

                  <label>
                    <span>或填写安全 HTTPS 外部下载链接（GitHub Release / 网盘外链）</span>
                    <input
                      type="url"
                      value={subExternalUrl}
                      onChange={(e) => setSubExternalUrl(e.target.value)}
                      placeholder="https://..."
                    />
                  </label>

                  <p className="workshop-detail-note">
                    安全说明：所有投稿压缩包都会检查可执行脚本黑名单、路径穿越，并记录 SHA-256 摘要；管理员审核通过后将对全部玩家开放下载与桌面端一键安装。
                  </p>
                </>
              )}

              {subError && <p className="extension-error">{subError}</p>}

              <footer className="workshop-modal-footer">
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={subSubmitting}
                  onClick={() => setSubmitOpen(false)}
                >
                  {subResult ? '完成' : '取消'}
                </button>
                {subResult ? (
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={() => setSubResult(null)}
                  >
                    继续投稿
                  </button>
                ) : (
                  <button type="submit" className="btn btn-primary" disabled={subSubmitting}>
                    <Upload size={14} />
                    {subSubmitting ? '正在校验并上传…' : '提交投稿'}
                  </button>
                )}
              </footer>
            </form>
          </div>
        </div>
      )}
    </section>
  );
}
