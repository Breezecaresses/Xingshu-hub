import React, { useEffect, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { CheckCircle2, Download, ExternalLink, X } from 'lucide-react';
import { useTranslation } from '../i18n';

export type AppUpdateRelease = {
  version: string;
  sequence: number;
  notes: string;
  desktop: { size: number };
  android: { versionCode: number };
};

export type AppUpdateCheckResult = {
  available: boolean;
  currentVersion: string;
  canApply: boolean;
  release: AppUpdateRelease | null;
  releaseUrl: string;
  downloadSource?: string;
  checkWarning?: string;
  message: string;
};

type UpdatePhase = 'idle' | 'downloading' | 'ready' | 'applying';

function formatSizeMb(bytes?: number): string {
  if (!bytes || bytes <= 0) return '';
  return `${(bytes / 1048576).toFixed(1)} MB`;
}

export const UpdatePromptController: React.FC = () => {
  const { locale } = useTranslation();
  const isZh = locale === 'zh-CN';

  const [result, setResult] = useState<AppUpdateCheckResult | null>(null);
  const [bannerDismissed, setBannerDismissed] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [phase, setPhase] = useState<UpdatePhase>('idle');
  const [error, setError] = useState('');

  const gateRef = useRef(false);
  const autoShownSeqRef = useRef<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    const timer = window.setTimeout(async () => {
      try {
        const next = await invoke<AppUpdateCheckResult>('check_app_update', { preview: true });
        if (cancelled) return;
        if (next.available && next.release) {
          setResult(next);
          if (autoShownSeqRef.current !== next.release.sequence) {
            autoShownSeqRef.current = next.release.sequence;
            setModalOpen(true);
          }
        }
      } catch {
        // Silent background check: dev builds without HubUpdater.exe or offline environments stay quiet.
      }
    }, 1100);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, []);

  if (!result?.available || !result.release) return null;
  const release = result.release;
  const busy = phase === 'downloading' || phase === 'applying';
  const sizeLabel = formatSizeMb(release.desktop?.size);
  const blockedReason = !result.canApply
    ? (isZh ? '请完整解压电脑版 ZIP，并从其中的 Hub 文件夹启动后即可一键在线更新。' : 'Extract the full Desktop ZIP and launch from Hub/ to enable one-click updates.')
    : (result.checkWarning || '');

  const handlePrimaryAction = async () => {
    if (gateRef.current || busy) return;
    gateRef.current = true;
    setError('');
    try {
      if (phase === 'ready') {
        setPhase('applying');
        await invoke('apply_app_update');
        return;
      }
      setPhase('downloading');
      const next = await invoke<AppUpdateCheckResult>('prepare_app_update', {
        preview: true,
        sequence: release.sequence,
      });
      setResult(next);
      setPhase('ready');
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      setError(msg || (isZh ? '下载或校验失败，请稍后重试。' : 'Failed to prepare update.'));
      setPhase('idle');
    } finally {
      gateRef.current = false;
    }
  };

  const handleLater = () => {
    if (busy) return;
    setModalOpen(false);
  };

  const handleDismissBanner = () => {
    if (busy) return;
    setBannerDismissed(true);
  };

  const ctaText = (() => {
    if (phase === 'downloading') return isZh ? '正在下载并校验更新包…' : 'Downloading & verifying…';
    if (phase === 'ready') return isZh ? `立即更新并重启 · ${release.version}` : `Install & Restart · ${release.version}`;
    if (phase === 'applying') return isZh ? '正在切换版本并重启 Hub…' : 'Applying update & restarting…';
    return isZh
      ? `下载并安装 ${release.version}${sizeLabel ? ` · ${sizeLabel}` : ''}`
      : `Download & Install ${release.version}${sizeLabel ? ` · ${sizeLabel}` : ''}`;
  })();

  return (
    <>
      {!bannerDismissed && (
        <div className="xingshu-update-banner" role="status" aria-live="polite">
          <div className="xingshu-update-banner-left">
            <span className="xingshu-update-banner-icon" aria-hidden="true">
              <Download size={14} />
            </span>
            <span className="xingshu-update-banner-title">
              {isZh ? `发现新版本 ${release.version}` : `New version ${release.version} available`}
            </span>
            {phase === 'ready' && (
              <span className="xingshu-update-pill-ready">{isZh ? '已就绪' : 'Ready'}</span>
            )}
          </div>
          <div className="xingshu-update-banner-actions">
            <button
              type="button"
              className="xingshu-update-banner-cta"
              onClick={() => setModalOpen(true)}
            >
              {isZh ? '查看更新' : 'View update'}
            </button>
            <button
              type="button"
              className="xingshu-update-banner-close"
              onClick={handleDismissBanner}
              aria-label={isZh ? '关闭更新提示' : 'Dismiss update notice'}
              title={isZh ? '暂不提示' : 'Dismiss'}
            >
              <X size={14} />
            </button>
          </div>
        </div>
      )}

      {modalOpen && (
        <div
          className="xingshu-update-modal-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="xingshu-update-modal-title"
        >
          <div className="xingshu-update-modal">
            <div className="xingshu-update-modal-top">
              <span className="xingshu-update-modal-badge" aria-hidden="true">
                {phase === 'ready' ? <CheckCircle2 size={20} /> : <Download size={20} />}
              </span>
              <button
                type="button"
                className="xingshu-update-modal-x"
                disabled={busy}
                onClick={handleLater}
                aria-label={isZh ? '关闭窗口' : 'Close dialog'}
              >
                <X size={17} />
              </button>
            </div>

            <h2 id="xingshu-update-modal-title" className="xingshu-update-modal-title">
              {isZh ? `发现新版本 ${release.version}` : `Update Available ${release.version}`}
            </h2>
            <p className="xingshu-update-modal-subtitle">
              {isZh ? `当前版本 ${result.currentVersion}` : `Current version ${result.currentVersion}`}
              {result.downloadSource ? ` · ${result.downloadSource}` : ''}
            </p>

            <div className="xingshu-update-notes-box">
              <div className="xingshu-update-notes-heading"># 星枢 Hub {release.version}</div>
              <pre className="xingshu-update-notes-body">
                {release.notes?.trim() || (isZh ? '修复已知问题并提升双端同步体验。' : 'Stability and UX improvements.')}
              </pre>
            </div>

            {phase === 'downloading' && (
              <div className="xingshu-update-progress-track" aria-hidden="true">
                <div className="xingshu-update-progress-bar" />
              </div>
            )}

            {blockedReason && (
              <p className="xingshu-update-warning">{blockedReason}</p>
            )}

            {error && (
              <p className="xingshu-update-error">{error}</p>
            )}

            {result.canApply && !result.checkWarning ? (
              <button
                type="button"
                className="xingshu-update-primary-cta"
                disabled={busy}
                onClick={() => void handlePrimaryAction()}
              >
                {phase === 'ready' ? <CheckCircle2 size={17} /> : <Download size={17} />}
                <span>{ctaText}</span>
              </button>
            ) : (
              <a
                className="xingshu-update-primary-cta"
                href={result.releaseUrl}
                target="_blank"
                rel="noreferrer"
              >
                <ExternalLink size={17} />
                <span>{isZh ? `前往 GitHub 下载 ${release.version}` : `Download ${release.version} on GitHub`}</span>
              </a>
            )}

            <button
              type="button"
              className="xingshu-update-later-btn"
              disabled={busy}
              onClick={handleLater}
            >
              {isZh ? '稍后再说' : 'Later'}
            </button>

            <p className="xingshu-update-footer-note">
              {isZh
                ? '安装前会校验官方签名与 SHA256，更新前会自动备份当前文件并保留手机配对身份。'
                : 'Signatures and SHA256 hashes are verified before applying updates.'}
            </p>
          </div>
        </div>
      )}
    </>
  );
};
