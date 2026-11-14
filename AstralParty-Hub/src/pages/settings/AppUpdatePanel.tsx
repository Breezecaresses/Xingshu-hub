import { useEffect } from 'react';
import { openUrl } from '@tauri-apps/plugin-opener';
import { CheckCircle2, Download, RefreshCw } from 'lucide-react';
import { useTranslation } from '../../i18n';
import { appUpdateStore, useAppUpdateState } from '../../shared/appUpdateStore';
import { PROJECT_SOURCE } from '../../shared/projectSource';

function formatCheckTime(timestamp: number | null, locale: string): string {
  if (!timestamp) return '';
  try {
    return new Date(timestamp).toLocaleTimeString(locale === 'zh-CN' ? 'zh-CN' : 'en-US', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  } catch {
    return '';
  }
}

type UpdateSwitchRowProps = {
  id: string;
  checked: boolean;
  disabled?: boolean;
  title: string;
  description: string;
  onChange: (checked: boolean) => void;
};

function UpdateSwitchRow({ id, checked, disabled, title, description, onChange }: UpdateSwitchRowProps) {
  const toggle = () => {
    if (!disabled) onChange(!checked);
  };
  return (
    <div
      className={`xingshu-update-switch-row${disabled ? ' is-disabled' : ''}`}
      onClick={toggle}
    >
      <div className="xingshu-update-switch-copy" id={`${id}-label`}>
        <span className="xingshu-update-switch-title">{title}</span>
        <span className="xingshu-update-switch-desc">{description}</span>
      </div>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        aria-labelledby={`${id}-label`}
        disabled={disabled}
        className={`xingshu-switch-track${checked ? ' is-on' : ''}`}
        onClick={(e) => {
          e.stopPropagation();
          toggle();
        }}
      >
        <span className="xingshu-switch-thumb" />
      </button>
    </div>
  );
}

export function AppUpdatePanel() {
  const { locale } = useTranslation();
  const isZh = locale === 'zh-CN';
  const state = useAppUpdateState();

  useEffect(() => {
    void appUpdateStore.ensureBuildLoaded();
  }, []);

  const { prefs, phase, result, error, lastAutoError, lastCheckedAt, isDesktopRuntime, buildVersion, buildLoadFailed } = state;
  const busy = phase === 'checking' || phase === 'downloading' || phase === 'applying';
  const checkedAtLabel = formatCheckTime(lastCheckedAt, locale);

  const versionText = !isDesktopRuntime
    ? (isZh ? '本地网页预览（构建版本与在线更新仅在桌面端生效）' : 'Web preview (build version & updates require desktop app)')
    : buildVersion
      ? buildVersion
      : buildLoadFailed
        ? (isZh ? '无法读取构建版本' : 'Unable to read build version')
        : (isZh ? '读取中…' : 'Loading…');

  return (
    <section className="panel">
      <div className="panel-header">
        <div className="xingshu-update-panel-heading">
          <h2 className="panel-title">
            <RefreshCw size={18} color="var(--primary)" />
            <span>{isZh ? '软件更新' : 'Software Updates'}</span>
          </h2>
          <span className={`xingshu-update-mode-pill${prefs.autoCheck ? ' is-auto' : ''}`}>
            {prefs.autoCheck
              ? (isZh ? '已开启自动接收更新' : 'Auto-check enabled')
              : (isZh ? '仅手动检查' : 'Manual checks only')}
          </span>
        </div>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={busy || !isDesktopRuntime}
          onClick={() => void appUpdateStore.checkForUpdates({ silent: false })}
        >
          {phase === 'checking'
            ? (isZh ? '正在检查…' : 'Checking…')
            : (isZh ? '检查更新' : 'Check for Updates')}
        </button>
      </div>

      <p className="xingshu-update-version-line">
        {isZh ? `当前 Hub：${versionText}` : `Current Hub: ${versionText}`}
        {checkedAtLabel && (
          <span className="xingshu-update-checked-at">
            {isZh ? ` · 上次检查 ${checkedAtLabel}` : ` · Last checked ${checkedAtLabel}`}
          </span>
        )}
      </p>

      <div className="xingshu-update-toggles" role="group" aria-label={isZh ? '软件更新偏好设置' : 'Software update preferences'}>
        <UpdateSwitchRow
          id="xingshu-pref-auto-check"
          checked={prefs.autoCheck}
          disabled={busy}
          title={isZh ? '自动接收更新推送' : 'Automatically receive updates'}
          description={isZh
            ? '启动星枢 Hub 时及每 30 分钟在后台静默检查官方签名新版本，发现可用更新时通过顶部提示条与弹窗通知。'
            : 'Automatically check for signed updates on startup and every 30 minutes while running.'}
          onChange={checked => appUpdateStore.setPreference('autoCheck', checked)}
        />
        <UpdateSwitchRow
          id="xingshu-pref-include-preview"
          checked={prefs.includePreview}
          disabled={busy}
          title={isZh ? '接收测试版 / 群测更新推送' : 'Receive preview / test releases'}
          description={isZh
            ? '与手机端安装助手保持一致，开启后同步接收 test / 群测先行补丁与新功能更新。'
            : 'Include signed pre-release and beta patches alongside stable releases.'}
          onChange={checked => appUpdateStore.setPreference('includePreview', checked)}
        />
        <UpdateSwitchRow
          id="xingshu-pref-auto-download"
          checked={prefs.autoDownload}
          disabled={busy || !prefs.autoCheck}
          title={isZh ? '发现新版本后自动后台下载安装包' : 'Auto-download & verify updates in background'}
          description={isZh
            ? '仅在已开启「自动接收更新」且可原地替换时生效：后台静默下载并校验 SHA256 与签名，就绪后仍需你点击确认再重启安装。'
            : 'Silently downloads and verifies SHA256 + signature when an applicable update is found; still waits for your click to restart.'}
          onChange={checked => appUpdateStore.setPreference('autoDownload', checked)}
        />
      </div>

      <p style={{ color: 'var(--text-muted)', lineHeight: 1.8 }}>
        {isZh
          ? '这里只更新 Hub。手机安装助手请在手机上点“检查助手更新”，新版 APK 需要确认安装。游戏内插件在“星枢包管理器”或手机助手中单独更新。'
          : 'This updates Desktop Hub only. Android Installer and in-game plugins update separately.'}
      </p>

      {phase === 'checking' && (
        <p role="status" style={{ marginTop: 14, color: 'var(--text-secondary)' }}>
          {isZh ? '正在检查可用更新并校验签名，网络异常时会在约 30 秒内结束。' : 'Checking for signed updates (times out within ~30s on network failure).'}
        </p>
      )}

      {!result && lastAutoError && phase === 'idle' && (
        <p role="status" style={{ marginTop: 12, color: 'var(--text-muted)', fontSize: 12.5 }}>
          {isZh ? `后台自动检查暂未完成：${lastAutoError}` : `Background auto-check did not complete: ${lastAutoError}`}
        </p>
      )}

      {result && (
        <div style={{ marginTop: 14, lineHeight: 1.8 }}>
          <p>{result.message}{result.downloadSource ? (isZh ? `（通道：${result.downloadSource}）` : ` (${result.downloadSource})`) : ''}</p>
          {result.checkWarning && <p role="status" style={{ color: 'var(--warning)' }}>{result.checkWarning}</p>}
          {result.available && result.release && (
            <>
              <p>
                <strong>{isZh ? `新版本 ${result.release.version}` : `New version ${result.release.version}`}</strong>
                {' · '}
                {(result.release.desktop.size / 1048576).toFixed(1)} MB
              </p>
              <p style={{ whiteSpace: 'pre-wrap', color: 'var(--text-secondary)' }}>{result.release.notes}</p>
              <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginTop: 12 }}>
                {phase === 'ready' ? (
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={busy}
                    onClick={() => void appUpdateStore.applyUpdate()}
                  >
                    <CheckCircle2 size={14} />
                    <span>{isZh ? '更新并重启 Hub' : 'Install & Restart Hub'}</span>
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary"
                    disabled={busy || !result.canApply || !!result.checkWarning}
                    onClick={() => void appUpdateStore.prepareUpdate()}
                  >
                    <Download size={14} />
                    <span>
                      {phase === 'downloading'
                        ? (isZh ? '下载和校验中，请稍候…' : 'Downloading & verifying…')
                        : (isZh ? '下载更新' : 'Download Update')}
                    </span>
                  </button>
                )}
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={appUpdateStore.openModal}
                >
                  {isZh ? '查看更新详情弹窗' : 'Open Release Dialog'}
                </button>
              </div>
              <p style={{ color: 'var(--text-muted)', marginTop: 10 }}>
                {result.canApply
                  ? (isZh
                    ? '请等待安装或还原任务完成，再更新并重启。更新时会暂停本包的手机连接服务，保留配对身份；下次连接手机时服务自动启动。更新前会备份本包文件。'
                    : 'Wait for active install/restore jobs before restarting. Phone pairing identity is preserved and files are backed up first.')
                  : (isZh
                    ? '请完整解压电脑版 ZIP，并从其中的 Hub 文件夹启动后更新。'
                    : 'Extract the full Desktop ZIP and run from the Hub folder to enable one-click apply.')}
              </p>
            </>
          )}
        </div>
      )}

      {error && (
        <p role="alert" style={{ marginTop: 12, color: 'var(--danger)' }}>
          {isZh ? `未完成：${error}。可重试或从官方发布页下载。` : `Failed: ${error}. Retry or download from Releases.`}
        </p>
      )}

      <p style={{ color: 'var(--text-muted)', fontSize: 12, marginTop: 10 }}>
        {isZh
          ? '检查与下载共用自动加速通道，后续版本也会自动选择响应较快的官方或国内镜像；所有更新包均强制校验签名与 SHA256。'
          : 'Auto-selected accelerated mirror channel with mandatory signature and SHA256 verification.'}
      </p>
      <button
        type="button"
        className="btn btn-secondary btn-sm"
        style={{ marginTop: 14 }}
        onClick={() => void openUrl(PROJECT_SOURCE + '/releases')}
      >
        {isZh ? '打开官方发布页 ↗' : 'Open Official Releases ↗'}
      </button>
    </section>
  );
}
