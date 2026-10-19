import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type {
  Backup,
  DiagnosticReport,
  GameInstallation,
  ModPackage,
  PlayerProfile,
  Plugin,
} from '../../shared/types';
import {
  Gamepad2,
  Puzzle,
  Sparkles,
  Archive,
  Activity,
  User,
  ShieldCheck,
  RefreshCw,
  FolderOpen,
  ArrowRight,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { useTranslation } from '../../i18n';

export const DashboardPage: React.FC = () => {
  const { t } = useTranslation();
  const [game, setGame] = useState<GameInstallation | null>(null);
  const [profile, setProfile] = useState<PlayerProfile | null>(null);
  const [plugins, setPlugins] = useState<Plugin[]>([]);
  const [mods, setMods] = useState<ModPackage[]>([]);
  const [backups, setBackups] = useState<Backup[]>([]);
  const [report, setReport] = useState<DiagnosticReport | null>(null);
  const [loading, setLoading] = useState(true);

  const refreshAll = async () => {
    setLoading(true);
    try {
      const [g, p, pl, mo, bk, rep] = await Promise.all([
        api.getPrimaryGame().catch(() => null),
        api.getPlayerProfile().catch(() => null),
        api.listPlugins().catch(() => []),
        api.listMods().catch(() => []),
        api.listBackups().catch(() => []),
        api.getDiagnosticReport().catch(() => null),
      ]);
      setGame(g);
      setProfile(p);
      setPlugins(pl);
      setMods(mo);
      setBackups(bk);
      setReport(rep);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    refreshAll();
  }, []);

  const handleAutoDetect = async () => {
    setLoading(true);
    try {
      const detected = await api.detectGame();
      setGame(detected);
      const rep = await api.getDiagnosticReport();
      setReport(rep);
    } catch (err: any) {
      alert(err.message || t('common.error'));
    } finally {
      setLoading(false);
    }
  };

  const lastBackupTime =
    backups.length > 0
      ? new Date(backups[0].created_at).toLocaleString()
      : t('dashboard.noBackups');

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 24, fontWeight: 700, letterSpacing: -0.5 }}>{t('dashboard.title')}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('dashboard.subtitle')}
          </p>
        </div>
        <div style={{ display: 'flex', gap: 10 }}>
          <button className="btn btn-secondary" onClick={refreshAll} disabled={loading}>
            <RefreshCw size={14} className={loading ? 'spin' : ''} />
            <span>{t('common.refresh')}</span>
          </button>
          <button className="btn btn-primary" onClick={handleAutoDetect} disabled={loading}>
            <Gamepad2 size={15} />
            <span>{t('dashboard.detectGame')}</span>
          </button>
        </div>
      </div>

      {/* 8 Primary Cards specified in Phase 0 Section 4 */}
      <div className="grid-cards">
        {/* Card 1: Astral Party Detection */}
        <div className="stat-card">
          <div className="stat-card-header">
            <span>{t('dashboard.astralParty')}</span>
            <Gamepad2 size={18} color="var(--primary)" />
          </div>
          <div className="stat-card-value" style={{ color: game?.is_valid ? 'var(--success)' : 'var(--danger)' }}>
            {game?.is_valid ? t('dashboard.detected') : t('dashboard.notDetected')}
          </div>
          <div className="stat-card-subtitle">
            {game?.platform ? `${t('dashboard.platformSteam')}` : t('dashboard.manualOrSteam')}
          </div>
        </div>

        {/* Card 2: Game Version / Build ID */}
        <div className="stat-card">
          <div className="stat-card-header">
            <span>{t('dashboard.buildId')}</span>
            <Activity size={18} color="var(--info)" />
          </div>
          <div className="stat-card-value">
            {game?.detected_build_id || t('common.unknown')}
          </div>
          <div className="stat-card-subtitle">
            Steam App ID: 2622000
          </div>
        </div>

        {/* Card 3: XingShu Runtime Status */}
        <div className="stat-card">
          <div className="stat-card-header">
            <span>{t('dashboard.runtimeStatus')}</span>
            <Puzzle size={18} color="var(--accent)" />
          </div>
          <div className="stat-card-value" style={{ color: report?.snapshot.apx_detected ? 'var(--success)' : 'var(--warning)' }}>
            {report?.snapshot.apx_detected ? t('dashboard.runtimeActive') : t('dashboard.runtimeNotFound')}
          </div>
          <div className="stat-card-subtitle">
            {report?.snapshot.apx_detected ? t('dashboard.runtimeDescActive') : t('dashboard.runtimeDescInactive')}
          </div>
        </div>

        {/* Card 4: Player Data */}
        <div className="stat-card">
          <div className="stat-card-header">
            <span>{t('dashboard.playerData')}</span>
            <User size={18} color="var(--primary)" />
          </div>
          <div className="stat-card-value">
            {profile ? t('dashboard.available') : t('dashboard.unavailable')}
          </div>
          <div className="stat-card-subtitle">
            {profile ? profile.display_name || t('dashboard.importedProfile') : t('dashboard.importOrSyncRequired')}
          </div>
        </div>

        {/* Card 5: Installed Plugins */}
        <div className="stat-card">
          <div className="stat-card-header">
            <span>{t('dashboard.installedPlugins')}</span>
            <Puzzle size={18} color="var(--accent)" />
          </div>
          <div className="stat-card-value">{plugins.length}</div>
          <div className="stat-card-subtitle">{t('dashboard.activeExtensions')}</div>
        </div>

        {/* Card 6: Installed Mods */}
        <div className="stat-card">
          <div className="stat-card-header">
            <span>{t('dashboard.installedMods')}</span>
            <Sparkles size={18} color="var(--warning)" />
          </div>
          <div className="stat-card-value">{mods.length}</div>
          <div className="stat-card-subtitle">{t('dashboard.modPackages')}</div>
        </div>

        {/* Card 7: Last Backup */}
        <div className="stat-card">
          <div className="stat-card-header">
            <span>{t('dashboard.lastBackup')}</span>
            <Archive size={18} color="var(--info)" />
          </div>
          <div className="stat-card-value" style={{ fontSize: 16, marginTop: 4 }}>
            {backups.length > 0 ? new Date(backups[0].created_at).toLocaleDateString() : t('dashboard.noBackups')}
          </div>
          <div className="stat-card-subtitle">{lastBackupTime}</div>
        </div>

        {/* Card 8: System Status */}
        <div className="stat-card">
          <div className="stat-card-header">
            <span>{t('dashboard.systemStatus')}</span>
            <ShieldCheck size={18} color={report?.overall_status === 'HEALTHY' ? 'var(--success)' : 'var(--warning)'} />
          </div>
          <div className="stat-card-value" style={{ color: report?.overall_status === 'HEALTHY' ? 'var(--success)' : 'var(--warning)' }}>
            {report?.overall_status === 'HEALTHY' ? t('common.healthy') : report?.overall_status === 'WARNING' ? t('common.warning') : (report?.overall_status || t('common.healthy'))}
          </div>
          <div className="stat-card-subtitle">
            {report?.items.length || 0} {t('dashboard.rulesEvaluated')}
          </div>
        </div>
      </div>

      {/* Main Panels */}
      <div style={{ display: 'grid', gridTemplateColumns: '1.4fr 1fr', gap: 20 }}>
        {/* Game Installation Details */}
        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">
              <FolderOpen size={18} color="var(--primary)" />
              <span>{t('dashboard.targetEnv')}</span>
            </h2>
            {game?.is_valid && (
              <span className="status-pill ok">
                <span className="pulse-dot" /> {t('dashboard.verifiedBadge')}
              </span>
            )}
          </div>

          {game ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <div className="code-box">
                <div><strong>{t('dashboard.installRoot')}:</strong> {game.install_path}</div>
                <div><strong>{t('dashboard.executable')}:</strong> {game.executable_path || t('common.notIdentified')}</div>
                <div><strong>{t('dashboard.buildId')}:</strong> {game.detected_build_id || 'N/A'}</div>
                <div><strong>{t('dashboard.detectionMethod')}:</strong> {game.detection_method}</div>
                <div><strong>{t('dashboard.lastVerified')}:</strong> {game.last_verified_at ? new Date(game.last_verified_at).toLocaleString() : t('common.justNow')}</div>
              </div>
              <div style={{ display: 'flex', gap: 10, marginTop: 4 }}>
                <Link to="/extensions/import" className="btn btn-primary btn-sm">
                  <span>{t('nav.importPackage')}</span>
                  <ArrowRight size={14} />
                </Link>
                <Link to="/diagnostics" className="btn btn-secondary btn-sm">
                  <span>{t('dashboard.viewDiagnostics')}</span>
                </Link>
              </div>
            </div>
          ) : (
            <div style={{ padding: 24, textAlign: 'center', color: 'var(--text-muted)' }}>
              <p>{t('dashboard.noGameDetected')}</p>
              <button className="btn btn-primary" style={{ marginTop: 12 }} onClick={handleAutoDetect}>
                {t('dashboard.runSteamDiscovery')}
              </button>
            </div>
          )}
        </div>

        {/* Quick Diagnostic Insights */}
        <div className="panel">
          <div className="panel-header">
            <h2 className="panel-title">
              <Activity size={18} color="var(--accent)" />
              <span>{t('dashboard.healthInsights')}</span>
            </h2>
            <Link to="/diagnostics" style={{ fontSize: 12, color: 'var(--primary)', textDecoration: 'none' }}>
              {t('dashboard.allRulesLink')}
            </Link>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {report?.items.slice(0, 3).map((item) => (
              <div
                key={item.rule_id}
                style={{
                  padding: '10px 14px',
                  borderRadius: 'var(--radius-sm)',
                  backgroundColor: 'var(--bg-secondary)',
                  border: '1px solid var(--border-color)',
                }}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <span style={{ fontWeight: 600, fontSize: 13 }}>{item.title}</span>
                  <span className={`status-pill ${item.severity.toLowerCase()}`} style={{ fontSize: 10, padding: '2px 6px' }}>
                    {item.severity}
                  </span>
                </div>
                <p style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4 }}>
                  {item.description}
                </p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
