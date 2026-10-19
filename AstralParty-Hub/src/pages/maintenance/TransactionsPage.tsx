import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { InstallTransaction } from '../../shared/types';
import { RefreshCw, RotateCcw, Clock } from 'lucide-react';
import { useTranslation } from '../../i18n';

export const TransactionsPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const [transactions, setTransactions] = useState<InstallTransaction[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchTransactions = () => {
    setLoading(true);
    api.listInstallTransactions(50)
      .then(setTransactions)
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    fetchTransactions();
  }, []);

  const isZh = locale === 'zh-CN';

  const handleRollback = async (txId: string) => {
    if (!confirm(t('maintenance.rollbackConfirm'))) {
      return;
    }
    try {
      await api.rollbackTransaction(txId);
      alert(isZh ? '事务已成功回滚！' : 'Transaction successfully rolled back!');
      fetchTransactions();
    } catch (e: any) {
      alert(`${isZh ? '回滚失败' : 'Rollback failed'}: ${e.message || e}`);
    }
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('maintenance.transactionsTitle')}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('maintenance.transactionsSubtitle')}
          </p>
        </div>
        <button className="btn btn-secondary" onClick={fetchTransactions} disabled={loading}>
          <RefreshCw size={14} className={loading ? 'spin' : ''} />
          <span>{t('common.refresh')}</span>
        </button>
      </div>

      <div className="panel">
        {transactions.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 36, color: 'var(--text-muted)' }}>
            <Clock size={36} style={{ marginBottom: 12, opacity: 0.5 }} />
            <p style={{ fontSize: 14 }}>{isZh ? '暂无安装事务记录。' : 'No installation transactions recorded yet.'}</p>
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>{isZh ? '事务标识' : 'Transaction ID'}</th>
                <th>{isZh ? '扩展包' : 'Package'}</th>
                <th>{isZh ? '操作类型' : 'Operation'}</th>
                <th>{t('common.status')}</th>
                <th>{isZh ? '发起时间' : 'Started At'}</th>
                <th>{isZh ? '关联备份' : 'Backup ID'}</th>
                <th style={{ textAlign: 'right' }}>{isZh ? '操作' : 'Actions'}</th>
              </tr>
            </thead>
            <tbody>
              {transactions.map((tx) => {
                const isSuccess = tx.state === 'COMPLETED';
                const isRolledBack = tx.state === 'ROLLED_BACK';
                const isFailed = tx.state.includes('FAIL');

                return (
                  <tr key={tx.id}>
                    <td><code>{tx.id.substring(0, 8)}...</code></td>
                    <td>
                      <div style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{tx.package_id}</div>
                      <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>v{tx.package_version}</div>
                    </td>
                    <td><span className="sidebar-tag">{tx.operation}</span></td>
                    <td>
                      <span
                        className={`status-pill ${
                          isSuccess ? 'ok' : isRolledBack ? 'info' : isFailed ? 'error' : 'warning'
                        }`}
                        style={{ fontSize: 11, padding: '2px 8px' }}
                      >
                        {tx.state}
                      </span>
                    </td>
                    <td>{new Date(tx.started_at).toLocaleString()}</td>
                    <td>
                      {tx.backup_id ? (
                        <code style={{ color: 'var(--info)' }}>{tx.backup_id.substring(0, 8)}...</code>
                      ) : (
                        <span style={{ color: 'var(--text-muted)' }}>{isZh ? '无' : 'None'}</span>
                      )}
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      {tx.backup_id && tx.state === 'COMPLETED' && (
                        <button
                          className="btn btn-secondary btn-sm"
                          onClick={() => handleRollback(tx.id)}
                          title={isZh ? '从备份还原原始文件' : 'Restore original files from backup'}
                        >
                          <RotateCcw size={12} />
                          <span>{t('maintenance.rollback')}</span>
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
};
