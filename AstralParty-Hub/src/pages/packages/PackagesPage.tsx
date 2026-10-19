import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { RemotePackage } from '../../shared/types';
import {
  Download,
  CheckCircle2,
  RefreshCw,
  Search,
} from 'lucide-react';
import { useTranslation } from '../../i18n';

export const PackagesPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const [packages, setPackages] = useState<RemotePackage[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<string>('ALL');
  const [search, setSearch] = useState<string>('');
  const [installingId, setInstallingId] = useState<string | null>(null);

  const fetchPackages = async () => {
    setLoading(true);
    try {
      const p = await api.listRemotePackages();
      setPackages(p);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchPackages();
  }, []);

  const handleInstall = async (pkg: RemotePackage) => {
    setInstallingId(pkg.id);
    try {
      await new Promise((r) => setTimeout(r, 1200));
      setPackages((prev) =>
        prev.map((item) =>
          item.id === pkg.id
            ? { ...item, installed_version: item.latest_version, has_update: false }
            : item
        )
      );
    } finally {
      setInstallingId(null);
    }
  };

  const filtered = packages.filter((pkg) => {
    if (filter !== 'ALL' && pkg.category !== filter) return false;
    if (search && !pkg.name.toLowerCase().includes(search.toLowerCase()) && !pkg.description.toLowerCase().includes(search.toLowerCase())) return false;
    return true;
  });

  const isZh = locale === 'zh-CN';

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('packages.title')}</h1>
        <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
          {t('packages.subtitle')}
        </p>
      </div>

      {/* Filter and Search Bar */}
      <div className="panel" style={{ marginBottom: 20 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 12 }}>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {['ALL', 'apx-plugin', 'mod', 'resource-pack'].map((cat) => (
              <button
                key={cat}
                className={`btn btn-sm ${filter === cat ? 'btn-primary' : 'btn-secondary'}`}
                onClick={() => setFilter(cat)}
              >
                {cat === 'ALL'
                  ? t('packages.allPackages')
                  : cat === 'apx-plugin'
                  ? t('packages.pluginsTab')
                  : cat === 'mod'
                  ? t('packages.modsTab')
                  : t('packages.resourceTab')}
              </button>
            ))}
          </div>

          <div style={{ display: 'flex', gap: 8 }}>
            <div style={{ position: 'relative', width: 220 }}>
              <input
                type="text"
                className="input-text"
                placeholder={t('packages.searchPlaceholder')}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                style={{ width: '100%', paddingLeft: 30 }}
              />
              <Search size={14} style={{ position: 'absolute', left: 10, top: 10, color: 'var(--text-muted)' }} />
            </div>

            <button className="btn btn-secondary btn-sm" onClick={fetchPackages} disabled={loading}>
              <RefreshCw size={14} className={loading ? 'spin' : ''} />
            </button>
          </div>
        </div>
      </div>

      {/* Packages Grid */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 16 }}>
        {filtered.map((pkg) => {
          const isInstalled = !!pkg.installed_version;
          const isBusy = installingId === pkg.id;

          return (
            <div
              key={pkg.id}
              className="panel"
              style={{
                margin: 0,
                display: 'flex',
                flexDirection: 'column',
                justifyContent: 'space-between',
                border: pkg.has_update ? '1px solid var(--warning)' : '1px solid var(--border-color)',
              }}
            >
              <div>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                  <div style={{ fontSize: 15, fontWeight: 700, color: '#fff' }}>
                    {pkg.name}
                  </div>
                  <span className="badge badge-default">
                    v{pkg.latest_version}
                  </span>
                </div>

                <div style={{ display: 'flex', gap: 8, marginTop: 6 }}>
                  <span className="badge badge-primary">{pkg.category}</span>
                  <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                    {t('packages.author')}: {pkg.publisher}
                  </span>
                </div>

                <p style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 10, lineHeight: 1.5 }}>
                  {pkg.description}
                </p>

                <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 12 }}>
                  {t('packages.size')}: {(pkg.size_bytes / (1024 * 1024)).toFixed(1)} MB | {t('packages.downloads')}: {pkg.downloads_count.toLocaleString()}
                </div>
              </div>

              <div style={{ marginTop: 16, paddingTop: 14, borderTop: '1px solid var(--border-color)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <div>
                  {isInstalled ? (
                    pkg.has_update ? (
                      <span className="badge badge-warning">{t('packages.updateAvailable')} ({isZh ? '当前' : 'Current'}: v{pkg.installed_version})</span>
                    ) : (
                      <span className="badge badge-success">
                        <CheckCircle2 size={12} style={{ marginRight: 4 }} />
                        {t('packages.installedLatest')}
                      </span>
                    )
                  ) : (
                    <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>{t('packages.notInstalled')}</span>
                  )}
                </div>

                <div>
                  {pkg.has_update ? (
                    <button
                      className="btn btn-primary btn-sm"
                      disabled={isBusy}
                      onClick={() => handleInstall(pkg)}
                    >
                      <Download size={14} />
                      <span>{isBusy ? t('packages.updating') : t('packages.update')}</span>
                    </button>
                  ) : !isInstalled ? (
                    <button
                      className="btn btn-primary btn-sm"
                      disabled={isBusy}
                      onClick={() => handleInstall(pkg)}
                    >
                      <Download size={14} />
                      <span>{isBusy ? t('packages.installing') : t('packages.install')}</span>
                    </button>
                  ) : (
                    <button className="btn btn-secondary btn-sm" disabled>
                      <span>{t('packages.installedReady')}</span>
                    </button>
                  )}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
