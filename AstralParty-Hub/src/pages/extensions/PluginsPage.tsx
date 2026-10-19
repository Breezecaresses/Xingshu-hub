import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { Plugin } from '../../shared/types';
import { Puzzle, Trash2, RefreshCw, Upload } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useTranslation } from '../../i18n';

export const PluginsPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const [plugins, setPlugins] = useState<Plugin[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchPlugins = () => {
    setLoading(true);
    api.listPlugins()
      .then(setPlugins)
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    fetchPlugins();
  }, []);

  const isZh = locale === 'zh-CN';

  const handleUninstall = async (pluginId: string) => {
    const confirmMsg = isZh
      ? `确定要卸载插件 '${pluginId}' 吗？`
      : `Are you sure you want to uninstall plugin '${pluginId}'?`;
    if (!confirm(confirmMsg)) return;
    try {
      await api.uninstallExtension(pluginId, true);
      fetchPlugins();
    } catch (e: any) {
      alert(`${isZh ? '卸载失败' : 'Uninstall failed'}: ${e.message || e}`);
    }
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700 }}>{isZh ? '运行时插件' : 'Runtime Plugins'}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {isZh ? '管理由星枢 / APX 加载器注入的运行时插件' : 'Manage in-game extensions executed via XingShu / APX loader'}
          </p>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          <button className="btn btn-secondary" onClick={fetchPlugins} disabled={loading}>
            <RefreshCw size={14} className={loading ? 'spin' : ''} />
            <span>{t('common.refresh')}</span>
          </button>
          <Link to="/extensions/import" className="btn btn-primary">
            <Upload size={14} />
            <span>{t('nav.importPackage')}</span>
          </Link>
        </div>
      </div>

      <div className="panel">
        {plugins.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 36, color: 'var(--text-muted)' }}>
            <Puzzle size={36} style={{ marginBottom: 12, opacity: 0.5 }} />
            <p style={{ fontSize: 14 }}>{isZh ? '当前未安装任何运行时插件。' : 'No runtime plugins currently installed.'}</p>
            <p style={{ fontSize: 12, marginTop: 4 }}>
              {isZh ? '导入符合规范的 .xspkg 或 .zip 扩展包进行安装。' : 'Import a compliant .xspkg or .zip package to install your first plugin.'}
            </p>
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>{isZh ? '插件名称' : 'Plugin Name'}</th>
                <th>{isZh ? '包标识' : 'Package ID'}</th>
                <th>{isZh ? '版本' : 'Version'}</th>
                <th>{t('common.status')}</th>
                <th>{isZh ? '安装时间' : 'Installed At'}</th>
                <th style={{ textAlign: 'right' }}>{isZh ? '操作' : 'Actions'}</th>
              </tr>
            </thead>
            <tbody>
              {plugins.map((p) => (
                <tr key={p.plugin_id}>
                  <td>
                    <div style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{p.name}</div>
                    <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{p.install_path || 'apx/plugins/'}</div>
                  </td>
                  <td><code>{p.plugin_id}</code></td>
                  <td>{p.installed_version || '1.0.0'}</td>
                  <td>
                    <span className="status-pill ok" style={{ fontSize: 11, padding: '2px 8px' }}>
                      {p.status}
                    </span>
                  </td>
                  <td>{p.installed_at ? new Date(p.installed_at).toLocaleDateString() : 'N/A'}</td>
                  <td style={{ textAlign: 'right' }}>
                    <button
                      className="btn btn-danger btn-sm"
                      onClick={() => handleUninstall(p.plugin_id)}
                      title={isZh ? '卸载插件' : 'Uninstall plugin'}
                    >
                      <Trash2 size={13} />
                      <span>{t('packages.uninstall')}</span>
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
};
