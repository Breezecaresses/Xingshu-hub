import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { Backup } from '../../shared/types';
import { Archive, RefreshCw, Folder } from 'lucide-react';
import { useTranslation } from '../../i18n';

export const BackupsPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const [backups, setBackups] = useState<Backup[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchBackups = () => {
    setLoading(true);
    api.listBackups()
      .then(setBackups)
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    fetchBackups();
  }, []);

  const isZh = locale === 'zh-CN';

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('maintenance.backupsTitle')}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('maintenance.backupsSubtitle')}
          </p>
        </div>
        <button className="btn btn-secondary" onClick={fetchBackups} disabled={loading}>
          <RefreshCw size={14} className={loading ? 'spin' : ''} />
          <span>{t('common.refresh')}</span>
        </button>
      </div>

      <div className="panel">
        {backups.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 36, color: 'var(--text-muted)' }}>
            <Archive size={36} style={{ marginBottom: 12, opacity: 0.5 }} />
            <p style={{ fontSize: 14 }}>{isZh ? '暂无备份记录。' : 'No backups recorded yet.'}</p>
            <p style={{ fontSize: 12, marginTop: 4 }}>
              {isZh ? '系统在修改或替换游戏文件前将自动创建备份快照。' : 'Backups are automatically taken before modifying or replacing game files.'}
            </p>
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>{isZh ? '备份标识' : 'Backup ID'}</th>
                <th>{isZh ? '目标包' : 'Target Package'}</th>
                <th>{isZh ? '类型' : 'Type'}</th>
                <th>{isZh ? '创建时间' : 'Created At'}</th>
                <th>{t('common.status')}</th>
                <th>{isZh ? '存储路径' : 'Storage Path'}</th>
              </tr>
            </thead>
            <tbody>
              {backups.map((b) => (
                <tr key={b.id}>
                  <td><code>{b.id.substring(0, 8)}...</code></td>
                  <td><strong>{b.target_id || (isZh ? '全局' : 'Global')}</strong></td>
                  <td><span className="sidebar-tag">{b.backup_type}</span></td>
                  <td>{new Date(b.created_at).toLocaleString()}</td>
                  <td>
                    <span className="status-pill ok" style={{ fontSize: 11, padding: '2px 8px' }}>
                      {b.status}
                    </span>
                  </td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 11, color: 'var(--text-muted)' }}>
                      <Folder size={12} />
                      <span>{b.storage_path}</span>
                    </div>
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
