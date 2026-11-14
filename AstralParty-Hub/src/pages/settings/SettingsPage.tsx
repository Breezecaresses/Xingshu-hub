import React, { useEffect, useState } from 'react';
import { openUrl } from '@tauri-apps/plugin-opener';
import { PROJECT_SOURCE, PROJECT_SOURCE_LABEL } from '../../shared/projectSource';
import { api } from '../../shared/api/tauri';
import type { GameInstallation, ProbeResult, ProviderMetadata } from '../../shared/types';
import { Folder, Save, Database, Shield, RefreshCw, Globe, Check } from 'lucide-react';
import { NavLink } from 'react-router-dom';
import { useTranslation } from '../../i18n';
import { AppUpdatePanel } from './AppUpdatePanel';

export const SettingsPage: React.FC = () => {
  const { locale, setLocale, t } = useTranslation();
  const [game, setGame] = useState<GameInstallation | null>(null);
  const [manualPath, setManualPath] = useState('');
  const [providers, setProviders] = useState<Array<[ProviderMetadata, ProbeResult]>>([]);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ text: string; isError: boolean } | null>(null);

  const loadData = () => {
    api.getPrimaryGame().then((g) => {
      setGame(g);
      if (g) setManualPath(g.install_path);
    });

    api.probeProviders().then(setProviders).catch(console.error);
  };

  useEffect(() => {
    loadData();
  }, []);

  const handleSavePath = async () => {
    if (!manualPath.trim()) return;
    setSaving(true);
    setMessage(null);
    try {
      const updated = await api.setManualGamePath(manualPath.trim());
      setGame(updated);
      setMessage({ text: t('settings.savedSuccess'), isError: false });
    } catch (e: any) {
      setMessage({ text: e.message || String(e), isError: true });
    } finally {
      setSaving(false);
    }
  };

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('settings.title')}</h1>
        <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
          {t('settings.subtitle')}
        </p>
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
        <AppUpdatePanel />
        {/* Language Selection Card */}
        <div className="panel">
          <h2 className="panel-title" style={{ marginBottom: 8 }}>
            <Globe size={18} color="var(--primary)" />
            <span>{t('settings.languageSection')}</span>
          </h2>
          <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 12 }}>
            {t('settings.languageDesc')}
          </p>

          <div className="lang-card-selector">
            <button
              type="button"
              className={`lang-card-btn ${locale === 'zh-CN' ? 'active' : ''}`}
              onClick={() => setLocale('zh-CN')}
            >
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' }}>
                <span className="lang-card-title">简体中文</span>
                {locale === 'zh-CN' && <Check size={16} color="var(--primary)" />}
              </div>
              <span className="lang-card-desc">纯中文界面，保留 XingShu 技术标识</span>
            </button>

            <button
              type="button"
              className={`lang-card-btn ${locale === 'en-US' ? 'active' : ''}`}
              onClick={() => setLocale('en-US')}
            >
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' }}>
                <span className="lang-card-title">English</span>
                {locale === 'en-US' && <Check size={16} color="var(--primary)" />}
              </div>
              <span className="lang-card-desc">Pure English interface with native technical terms</span>
            </button>
          </div>
        </div>

        {/* Game Installation Path */}
        <div className="panel">
          <h2 className="panel-title" style={{ marginBottom: 14 }}>
            <Folder size={18} color="var(--primary)" />
            <span>{t('settings.targetInstallation')}</span>
          </h2>
          <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginBottom: 14 }}>
            {t('settings.targetInstallationDesc')}
          </p>

          <div style={{ display: 'flex', gap: 10 }}>
            <input
              type="text"
              className="form-input"
              style={{ flex: 1 }}
              value={manualPath}
              onChange={(e) => setManualPath(e.target.value)}
              placeholder={t('settings.pathPlaceholder')}
            />
            <button className="btn btn-primary" onClick={handleSavePath} disabled={saving}>
              <Save size={14} />
              <span>{saving ? t('settings.saving') : t('settings.validateAndSave')}</span>
            </button>
          </div>

          {message && (
            <div
              style={{
                marginTop: 12,
                padding: '8px 12px',
                borderRadius: 6,
                backgroundColor: message.isError ? 'var(--danger-bg)' : 'var(--success-bg)',
                color: message.isError ? 'var(--danger)' : 'var(--success)',
                fontSize: 13,
              }}
            >
              {message.text}
            </div>
          )}

          {game && (
            <div className="code-box" style={{ marginTop: 14 }}>
              <div><strong>{t('settings.activePath')}:</strong> {game.install_path}</div>
              <div><strong>{t('settings.detectionMethod')}:</strong> {game.detection_method} ({t('common.status')}: {game.is_valid ? t('common.yes') : t('common.no')})</div>
              <div><strong>{t('settings.buildId')}:</strong> {game.detected_build_id || 'N/A'}</div>
            </div>
          )}
        </div>

        {/* Data Providers */}
        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">
              <Database size={18} color="var(--accent)" />
              <span>{t('diagnostics.providerStatus')}</span>
            </h2>
            <button className="btn btn-secondary btn-sm" onClick={loadData}>
              <RefreshCw size={13} />
              <span>{t('diagnostics.runProbe')}</span>
            </button>
          </div>

          <table className="data-table">
            <thead>
              <tr>
                <th>{t('diagnostics.providerName')}</th>
                <th>{t('diagnostics.providerType')}</th>
                <th>{t('diagnostics.version')}</th>
                <th>{t('diagnostics.status')}</th>
                <th>{t('diagnostics.description')}</th>
              </tr>
            </thead>
            <tbody>
              {providers.map(([meta, probe]) => (
                <tr key={meta.id}>
                  <td><strong>{meta.name}</strong></td>
                  <td><code>{meta.provider_type}</code></td>
                  <td>{meta.version}</td>
                  <td>
                    <span className={`status-pill ${probe.available ? 'ok' : 'warning'}`} style={{ fontSize: 11, padding: '2px 8px' }}>
                      {probe.status_message}
                    </span>
                  </td>
                  <td style={{ fontSize: 12, color: 'var(--text-muted)' }}>{meta.description}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="panel">
          <h2 className="panel-title" style={{ marginBottom: 12 }}>星枢项目来源</h2>
          <p style={{ color: 'var(--text-secondary)', lineHeight: 1.8 }}>Hub 与游戏内运行时源码：{PROJECT_SOURCE_LABEL}</p>
          <button type="button" className="btn btn-secondary" onClick={() => void openUrl(PROJECT_SOURCE)}>{PROJECT_SOURCE} ↗</button>
          <p style={{ color: 'var(--text-muted)', marginTop: 12 }}>代码许可见项目仓库的 LICENSE；角色、卡面及游戏配置素材来源于吉星派对，相关权利归原权利人所有。</p>
        </div>

        {/* 资源冲突防线与运维快捷中心 */}
        <div className="panel">
          <h2 className="panel-title" style={{ marginBottom: 12 }}>
            <Shield size={18} color="var(--info)" />
            <span>{locale === 'zh-CN' ? '资源冲突防线与运维中心' : 'Resource Conflict Guard & Maintenance'}</span>
          </h2>
          <div style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.7 }}>
            <p>• 自动排查 APX 残留、双代理加载器（<code>winhttp.dll</code> / <code>version.dll</code>）与多目录同名模组 DLL 冲突。</p>
            <p>• 所有插件安装、替换与卸载均自动生成事务快照，支持随时一键无损还原。</p>
          </div>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginTop: 14 }}>
            <NavLink to="/extensions/packages" className="btn btn-secondary">星枢包管理器 →</NavLink>
            <NavLink to="/maintenance/transactions" className="btn btn-secondary">安装事务记录 →</NavLink>
            <NavLink to="/maintenance/backups" className="btn btn-secondary">数据备份与还原 →</NavLink>
            <NavLink to="/diagnostics" className="btn btn-secondary">系统诊断中心 →</NavLink>
          </div>
        </div>
      </div>
    </div>
  );
};
