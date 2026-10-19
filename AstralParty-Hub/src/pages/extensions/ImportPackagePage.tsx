import React, { useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { HubPackageManifestV1, InstallTransaction } from '../../shared/types';
import { Upload, CheckCircle2, ShieldCheck, AlertTriangle, FileArchive, ArrowRight, RefreshCw } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useTranslation } from '../../i18n';

export const ImportPackagePage: React.FC = () => {
  const { locale, t } = useTranslation();
  const [zipPath, setZipPath] = useState('');
  const [manifest, setManifest] = useState<HubPackageManifestV1 | null>(null);
  const [loading, setLoading] = useState(false);
  const [installing, setInstalling] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [transaction, setTransaction] = useState<InstallTransaction | null>(null);

  const handleInspect = async () => {
    if (!zipPath.trim()) return;
    setLoading(true);
    setError(null);
    setManifest(null);
    setTransaction(null);

    try {
      const parsed = await api.inspectPackageArchive(zipPath.trim());
      setManifest(parsed);
    } catch (e: any) {
      setError(e.message || String(e));
    } finally {
      setLoading(false);
    }
  };

  const handleInstall = async () => {
    if (!zipPath.trim()) return;
    setInstalling(true);
    setError(null);

    try {
      const tx = await api.installPackage(zipPath.trim());
      setTransaction(tx);
    } catch (e: any) {
      setError(e.message || String(e));
    } finally {
      setInstalling(false);
    }
  };

  const isZh = locale === 'zh-CN';

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('packages.importTitle')}</h1>
        <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
          {t('packages.importSubtitle')}
        </p>
      </div>

      <div className="panel">
        <h2 className="panel-title" style={{ marginBottom: 12 }}>
          <FileArchive size={18} color="var(--primary)" />
          <span>{isZh ? '选择扩展包归档' : 'Select Package Archive'}</span>
        </h2>
        <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
          <input
            type="text"
            className="form-input"
            style={{ flex: 1 }}
            placeholder={isZh ? '输入扩展包绝对路径 (支持 .xspkg, .zip, .apx 等)' : 'Enter absolute path to .xspkg or .zip package'}
            value={zipPath}
            onChange={(e) => setZipPath(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleInspect()}
          />
          <button className="btn btn-primary" onClick={handleInspect} disabled={loading || !zipPath}>
            {loading ? <RefreshCw size={14} className="spin" /> : <Upload size={14} />}
            <span>{t('packages.inspectPackage')}</span>
          </button>
        </div>

        {error && (
          <div style={{ marginTop: 14, padding: 12, borderRadius: 6, backgroundColor: 'var(--danger-bg)', border: '1px solid rgba(239, 68, 68, 0.3)', color: 'var(--danger)', fontSize: 13, display: 'flex', alignItems: 'center', gap: 8 }}>
            <AlertTriangle size={16} />
            <span>{error}</span>
          </div>
        )}
      </div>

      {manifest && (
        <div className="panel">
          <div className="panel-header">
            <div>
              <h2 className="panel-title">
                <ShieldCheck size={18} color="var(--success)" />
                <span>{manifest.package.name}</span>
                <span className="sidebar-tag">{manifest.package.version}</span>
              </h2>
              <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>
                {t('packages.author')}: <strong>{manifest.package.publisher}</strong> | {isZh ? '类型' : 'Type'}: <code>{manifest.package.type}</code>
              </div>
            </div>
            {!transaction && (
              <button className="btn btn-primary" onClick={handleInstall} disabled={installing}>
                {installing ? (
                  <>
                    <RefreshCw size={14} className="spin" />
                    <span>{isZh ? '正在提交事务...' : 'Committing Transaction...'}</span>
                  </>
                ) : (
                  <>
                    <CheckCircle2 size={15} />
                    <span>{t('packages.installPackage')}</span>
                  </>
                )}
              </button>
            )}
          </div>

          <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 16 }}>
            {manifest.package.description || (isZh ? '未提供描述。' : 'No description provided.')}
          </p>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 12, marginBottom: 16 }}>
            <div className="stat-card" style={{ padding: 12 }}>
              <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{isZh ? '游戏兼容性' : 'Game Compatibility'}</div>
              <div style={{ fontSize: 13, fontWeight: 600, marginTop: 4 }}>
                Steam App ID: {manifest.compatibility.game.appId}
              </div>
            </div>
            <div className="stat-card" style={{ padding: 12 }}>
              <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{isZh ? '安全门禁' : 'Safety Gates'}</div>
              <div style={{ fontSize: 13, fontWeight: 600, marginTop: 4 }}>
                {isZh ? '需关闭游戏' : 'Requires Game Closed'}: {manifest.install.requiresGameClosed ? t('common.yes') : t('common.no')}
              </div>
            </div>
            <div className="stat-card" style={{ padding: 12 }}>
              <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{isZh ? '自动快照' : 'Automated Snapshot'}</div>
              <div style={{ fontSize: 13, fontWeight: 600, marginTop: 4 }}>
                {isZh ? '安装前备份' : 'Pre-install Backup'}: {manifest.install.backup ? (isZh ? '已启用' : 'Enabled') : (isZh ? '未启用' : 'Disabled')}
              </div>
            </div>
          </div>

          <h3 style={{ fontSize: 14, fontWeight: 600, marginBottom: 8 }}>{isZh ? '目标文件映射清单' : 'Target File Mappings'}</h3>
          <table className="data-table">
            <thead>
              <tr>
                <th>{isZh ? '源文件' : 'Source Payload'}</th>
                <th>{isZh ? '目标逻辑路径' : 'Logical Target'}</th>
                <th>{isZh ? '文件体积' : 'Declared Size'}</th>
                <th>{isZh ? 'SHA256 校验和' : 'SHA256 Checksum'}</th>
              </tr>
            </thead>
            <tbody>
              {manifest.files.map((f, i) => (
                <tr key={i}>
                  <td><code>{f.source}</code></td>
                  <td><code>{f.target}</code></td>
                  <td>{(f.size / 1024).toFixed(1)} KB</td>
                  <td>
                    <code style={{ fontSize: 10, color: 'var(--text-muted)' }}>
                      {f.sha256.substring(0, 16)}...
                    </code>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {transaction && (
        <div className="panel" style={{ border: '1px solid rgba(16, 185, 129, 0.4)' }}>
          <div className="panel-header">
            <h2 className="panel-title" style={{ color: 'var(--success)' }}>
              <CheckCircle2 size={18} />
              <span>{isZh ? '安装事务执行完成' : 'Installation Transaction Completed'}</span>
            </h2>
            <Link to="/maintenance/transactions" className="btn btn-secondary btn-sm">
              <span>{isZh ? '查看历史' : 'View History'}</span>
              <ArrowRight size={13} />
            </Link>
          </div>
          <div className="code-box">
            <div><strong>Transaction ID:</strong> {transaction.id}</div>
            <div><strong>Package:</strong> {transaction.package_id} v{transaction.package_version}</div>
            <div><strong>State:</strong> <span style={{ color: 'var(--success)', fontWeight: 600 }}>{transaction.state}</span></div>
            <div><strong>Backup ID:</strong> {transaction.backup_id || (isZh ? '无' : 'None')}</div>
            <div><strong>Started At:</strong> {new Date(transaction.started_at).toLocaleString()}</div>
          </div>
        </div>
      )}
    </div>
  );
};
