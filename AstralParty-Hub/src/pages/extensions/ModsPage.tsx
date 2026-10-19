import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { ModPackage } from '../../shared/types';
import { Sparkles, Trash2, RefreshCw, Upload } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useTranslation } from '../../i18n';

export const ModsPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const [mods, setMods] = useState<ModPackage[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchMods = () => {
    setLoading(true);
    api.listMods()
      .then(setMods)
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    fetchMods();
  }, []);

  const isZh = locale === 'zh-CN';

  const handleUninstall = async (modId: string) => {
    const confirmMsg = isZh
      ? `确定要卸载模组 '${modId}' 吗？`
      : `Are you sure you want to uninstall mod '${modId}'?`;
    if (!confirm(confirmMsg)) return;
    try {
      await api.uninstallExtension(modId, false);
      fetchMods();
    } catch (e: any) {
      alert(`${isZh ? '卸载失败' : 'Uninstall failed'}: ${e.message || e}`);
    }
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700 }}>{isZh ? '游戏模组' : 'Game Mods'}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {isZh ? '管理本地部署的模型、贴图与资源替换包' : 'Manage client graphics, sounds, and texture modifications'}
          </p>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          <button className="btn btn-secondary" onClick={fetchMods} disabled={loading}>
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
        {mods.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 36, color: 'var(--text-muted)' }}>
            <Sparkles size={36} style={{ marginBottom: 12, opacity: 0.5 }} />
            <p style={{ fontSize: 14 }}>{isZh ? '当前未安装任何模组。' : 'No visual mods currently installed.'}</p>
            <p style={{ fontSize: 12, marginTop: 4 }}>
              {isZh ? '导入类型为 mod 或 resource-pack 的 .xspkg 或 .zip 扩展包进行安装。' : 'Import a .xspkg or .zip package to apply visual customisations.'}
            </p>
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>{isZh ? '模组名称' : 'Mod Title'}</th>
                <th>{isZh ? '包标识' : 'Package ID'}</th>
                <th>{isZh ? '版本' : 'Version'}</th>
                <th>{t('common.status')}</th>
                <th>{isZh ? '安装时间' : 'Installed At'}</th>
                <th style={{ textAlign: 'right' }}>{isZh ? '操作' : 'Actions'}</th>
              </tr>
            </thead>
            <tbody>
              {mods.map((m) => (
                <tr key={m.mod_id}>
                  <td>
                    <div style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{m.name}</div>
                    <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{m.install_path || 'game/mods/'}</div>
                  </td>
                  <td><code>{m.mod_id}</code></td>
                  <td>{m.installed_version || '1.0.0'}</td>
                  <td>
                    <span className="status-pill ok" style={{ fontSize: 11, padding: '2px 8px' }}>
                      {m.status}
                    </span>
                  </td>
                  <td>{m.installed_at ? new Date(m.installed_at).toLocaleDateString() : 'N/A'}</td>
                  <td style={{ textAlign: 'right' }}>
                    <button
                      className="btn btn-danger btn-sm"
                      onClick={() => handleUninstall(m.mod_id)}
                      title={isZh ? '卸载模组' : 'Uninstall mod'}
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
