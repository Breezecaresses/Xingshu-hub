import React from 'react';
import { openUrl } from '@tauri-apps/plugin-opener';
import { PROJECT_SOURCE, PROJECT_SOURCE_LABEL } from '../../shared/projectSource';
import { NavLink } from 'react-router-dom';
import {
  LayoutDashboard,
  User,
  UserCheck,
  Users,
  Layers,
  Coins,
  Map,
  History,
  Sparkles,
  RefreshCw,
  Archive,
  Activity,
  Settings,
  Eye,
  Radio,
  PackageCheck,
} from 'lucide-react';
import { useTranslation } from '../../i18n';

export const Sidebar: React.FC = () => {
  const { locale, t } = useTranslation();

  return (
    <aside className="sidebar">
      <div className="sidebar-header">
        <div className="sidebar-logo">
          <img src="/brand/xingshu.svg" alt="" aria-hidden="true" />
        </div>
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          <div style={{ display: 'flex', alignItems: 'center' }}>
            <span className="sidebar-brand">{locale === 'zh-CN' ? '星枢 Hub' : 'XingShu Hub'}</span>
            <span className="sidebar-tag">XingShu</span>
          </div>
          <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{t('common.platformSubtitle')}</span>
        </div>
      </div>

      <nav className="nav-menu">
        <NavLink
          to="/dashboard"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <LayoutDashboard size={17} />
          <span>{t('nav.overview')}</span>
        </NavLink>

        <div className="nav-section-title">{t('nav.playerData')}</div>
        <NavLink
          to="/player/profile"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <User size={17} />
          <span>{t('nav.profile')}</span>
        </NavLink>
        <NavLink
          to="/player/accounts"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <UserCheck size={17} />
          <span>{t('nav.accounts')}</span>
        </NavLink>
        <NavLink
          to="/player/characters"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Users size={17} />
          <span>{t('nav.characters')}</span>
        </NavLink>
        <NavLink
          to="/player/cards"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Layers size={17} />
          <span>{t('nav.cards')}</span>
        </NavLink>
        <NavLink
          to="/player/chips"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Coins size={17} />
          <span>{locale === 'zh-CN' ? '筹码图鉴' : 'Battle tokens'}</span>
        </NavLink>
        <NavLink
          to="/player/maps"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Map size={17} />
          <span>{locale === 'zh-CN' ? '地图与场地' : 'Maps & fields'}</span>
        </NavLink>
        <NavLink
          to="/player/matches"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <History size={17} />
          <span>{t('nav.matches')}</span>
        </NavLink>

        <div className="nav-section-title">{t('nav.xingshuCore')}</div>
        <NavLink
          to="/spectator"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Eye size={17} />
          <span>{t('nav.spectator')}</span>
        </NavLink>
        <NavLink
          to="/bridge"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Radio size={17} />
          <span>{t('nav.bridge')}</span>
        </NavLink>

        <div className="nav-section-title">{t('nav.packagesAndExtensions')}</div>
        <NavLink
          to="/extensions/packages"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <PackageCheck size={17} />
          <span>{t('nav.packageManager')}</span>
        </NavLink>
        <NavLink
          to="/extensions/mods"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Sparkles size={17} />
          <span>{t('nav.gameMods')}</span>
        </NavLink>

        <div className="nav-section-title">{t('nav.operations')}</div>
        <NavLink
          to="/maintenance/transactions"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <RefreshCw size={17} />
          <span>{t('nav.transactions')}</span>
        </NavLink>
        <NavLink
          to="/maintenance/backups"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Archive size={17} />
          <span>{t('nav.backups')}</span>
        </NavLink>
        <NavLink
          to="/diagnostics"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Activity size={17} />
          <span>{t('nav.diagnostics')}</span>
        </NavLink>
        <NavLink
          to="/settings"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Settings size={17} />
          <span>{t('nav.settings')}</span>
        </NavLink>
      </nav>

      <div className="sidebar-footer">
        <button type="button" title={`项目来源：${PROJECT_SOURCE_LABEL}`} onClick={() => void openUrl(PROJECT_SOURCE)} style={{ color: 'var(--text-secondary)', border: 0, background: 'none', cursor: 'pointer', padding: 0 }}>项目来源 ↗ GitHub</button>
        <span style={{ color: 'var(--success)', fontWeight: 600 }}>{t('common.versionReady')}</span>
      </div>
    </aside>
  );
};
