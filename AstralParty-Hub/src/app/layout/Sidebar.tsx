import React from 'react';
import { assetUrl, isTauriRuntime } from '../../shared/runtime';
import { Sparkles as ReplayIcon } from 'lucide-react';
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
  Activity,
  Settings,
  Eye,
  Radio,
  PackageCheck,
  X,
} from 'lucide-react';
import { useTranslation } from '../../i18n';
import { GitHubRepoLink } from '../../components/GitHubRepoLink';

export interface SidebarProps {
  mobileOpen?: boolean;
  onClose?: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ mobileOpen = false, onClose }) => {
  const { locale, t } = useTranslation();
  const isDesktop = isTauriRuntime();

  return (
    <aside className={`sidebar${mobileOpen ? ' is-mobile-open' : ''}`}>
      <div className="sidebar-header">
        <div className="sidebar-logo">
          <img src={assetUrl('brand/xingshu.svg')} alt="" aria-hidden="true" />
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center' }}>
            <span className="sidebar-brand aurora-sheen-text">{locale === 'zh-CN' ? '星枢 Hub' : 'XingShu Hub'}</span>
            <span className="sidebar-tag">XingShu</span>
          </div>
          <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{t('common.platformSubtitle')}</span>
        </div>
        <button
          type="button"
          className="mobile-sidebar-close"
          aria-label={locale === 'zh-CN' ? '关闭导航菜单' : 'Close navigation menu'}
          onClick={() => onClose?.()}
        >
          <X size={17} />
        </button>
      </div>

      <nav
        className="nav-menu"
        onClick={(e) => {
          if ((e.target as HTMLElement).closest('a')) onClose?.();
        }}
      >
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
        <NavLink
          to="/player/replay"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <ReplayIcon size={17} />
          <span>{locale === 'zh-CN' ? '回放分析' : 'Replay Lab'}</span>
        </NavLink>

        {isDesktop && (
          <>
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
          </>
        )}

        <div className="nav-section-title">{t('nav.packagesAndExtensions')}</div>
        {isDesktop && (
          <NavLink
            to="/extensions/packages"
            className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
          >
            <PackageCheck size={17} />
            <span>{t('nav.packageManager')}</span>
          </NavLink>
        )}
        <NavLink
          to="/extensions/mods"
          className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
        >
          <Sparkles size={17} />
          <span>{t('nav.gameMods')}</span>
        </NavLink>

        {isDesktop && (
          <>
            <div className="nav-section-title">{t('nav.operations')}</div>
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
          </>
        )}
      </nav>

      <div className="sidebar-footer">
        <GitHubRepoLink
          variant="inline"
          label={isDesktop ? 'GitHub · XingShu' : 'AstralParty-XingShu'}
        />
        <span className="sidebar-footer-status" style={{ color: 'var(--success)', fontWeight: 600 }}>{isDesktop ? t('common.versionReady') : 'Web'}</span>
      </div>
    </aside>
  );
};
