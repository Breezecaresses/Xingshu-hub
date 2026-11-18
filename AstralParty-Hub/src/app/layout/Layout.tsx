import React, { useEffect, useRef, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { api } from '../../shared/api/tauri';
import type { DiagnosticReport } from '../../shared/types';
import { ShieldCheck, AlertTriangle, Menu } from 'lucide-react';
import { useTranslation, LanguageSwitcher } from '../../i18n';
import { UpdatePromptController } from '../../components/UpdatePromptModal';
import { getCurrentWebviewWindow } from '@tauri-apps/api/webviewWindow';
import { isTauriRuntime } from '../../shared/runtime';
import { GitHubRepoLink } from '../../components/GitHubRepoLink';
import { DesktopWindowBar } from './DesktopWindowBar';

export const Layout: React.FC = () => {
  const [report, setReport] = useState<DiagnosticReport | null>(null);
  const { locale, t } = useTranslation();
  const shellRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLElement>(null);
  const [isScrolled, setIsScrolled] = useState(false);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const { pathname } = useLocation();

  useEffect(() => {
    setMobileNavOpen(false);
    const el = contentRef.current;
    if (el) {
      el.scrollTo(0, 0);
      setIsScrolled(false);
      shellRef.current?.style.setProperty('--page-progress', '0');
    }
  }, [pathname]);

  useEffect(() => {
    if (!mobileNavOpen) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setMobileNavOpen(false);
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [mobileNavOpen]);

  useEffect(() => {
    const scroller = contentRef.current;
    if (!scroller) return;
    let scrollQueued = false;
    const syncScroll = () => {
      scrollQueued = false;
      const top = scroller.scrollTop;
      const max = scroller.scrollHeight - scroller.clientHeight;
      const progress = max > 0 ? Math.min(1, Math.max(0, top / max)) : 0;
      setIsScrolled(top > 12);
      shellRef.current?.style.setProperty('--page-progress', progress.toFixed(4));
    };
    const onScroll = () => {
      if (!scrollQueued) {
        scrollQueued = true;
        requestAnimationFrame(syncScroll);
      }
    };
    syncScroll();
    scroller.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll, { passive: true });
    return () => {
      scroller.removeEventListener('scroll', onScroll);
      window.removeEventListener('resize', onScroll);
    };
  }, []);

  useEffect(() => {
    const shell = shellRef.current;
    if (!shell) return;
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    const fine = window.matchMedia?.('(hover: hover) and (pointer: fine)').matches;
    if (reduced || !fine) return;

    let aimX = 0;
    let aimY = 0;
    let curX = 0;
    let curY = 0;
    let auroraFrame = 0;
    let spotFrame = 0;
    let lastEvent: PointerEvent | null = null;

    const spotlightSelector =
      '.panel, .card, .stat-card, .library-summary, .library-links a, .library-tile, .library-record, .extension-summary > div, .mode-card, .field-effect';

    const driftAurora = () => {
      curX += (aimX - curX) * 0.085;
      curY += (aimY - curY) * 0.085;
      shell.style.setProperty('--aurora-x', curX.toFixed(3));
      shell.style.setProperty('--aurora-y', curY.toFixed(3));
      auroraFrame =
        Math.abs(aimX - curX) + Math.abs(aimY - curY) > 0.002
          ? requestAnimationFrame(driftAurora)
          : 0;
    };

    const onPointerMove = (event: PointerEvent) => {
      const rect = shell.getBoundingClientRect();
      if (rect.width > 0 && rect.height > 0) {
        aimX = ((event.clientX - rect.left) / rect.width - 0.5) * 2;
        aimY = ((event.clientY - rect.top) / rect.height - 0.5) * 2;
        if (!auroraFrame) {
          auroraFrame = requestAnimationFrame(driftAurora);
        }
      }

      lastEvent = event;
      if (spotFrame) return;
      spotFrame = requestAnimationFrame(() => {
        spotFrame = 0;
        if (!lastEvent || !(lastEvent.target instanceof Element)) return;
        const surface = lastEvent.target.closest<HTMLElement>(spotlightSelector);
        if (!surface) return;
        const surfaceRect = surface.getBoundingClientRect();
        surface.style.setProperty('--spot-x', `${Math.round(lastEvent.clientX - surfaceRect.left)}px`);
        surface.style.setProperty('--spot-y', `${Math.round(lastEvent.clientY - surfaceRect.top)}px`);
      });
    };

    shell.addEventListener('pointermove', onPointerMove, { passive: true });
    return () => {
      shell.removeEventListener('pointermove', onPointerMove);
      if (auroraFrame) cancelAnimationFrame(auroraFrame);
      if (spotFrame) cancelAnimationFrame(spotFrame);
    };
  }, []);

  useEffect(() => {
    if (!isTauriRuntime()) {
      setReport(null);
      return;
    }
    const refresh = () => api.getDiagnosticReport()
      .then(setReport)
      .catch(() => setReport(null));
    void refresh();
    const timer = setInterval(() => void refresh(), 10000);
    return () => clearInterval(timer);
  }, []);

  const appTitle = locale === 'zh-CN' ? '星枢 Hub' : 'XingShu Hub';
  const webMode = !isTauriRuntime();

  useEffect(() => {
    if (typeof document !== 'undefined') {
      document.title = webMode ? `${appTitle} Web` : appTitle;
    }
    if (!isTauriRuntime()) return;
    try {
      const appWin = getCurrentWebviewWindow();
      appWin.setTitle(appTitle).catch((err) => {
        console.warn('Failed to set window title via Tauri:', err);
      });
    } catch {
      // Ignored in non-Tauri environment
    }
  }, [appTitle, webMode]);

  const statusClass =
    report?.overall_status === 'HEALTHY'
      ? 'healthy'
      : report?.overall_status === 'WARNING'
      ? 'warning'
      : 'degraded';

  const statusLabel =
    report?.overall_status === 'HEALTHY'
      ? t('common.healthy')
      : report?.overall_status === 'WARNING'
      ? t('common.warning')
      : report?.overall_status === 'DEGRADED'
      ? t('common.degraded')
      : t('common.initializing');
  return (
    <div className={webMode ? 'web-app-frame' : 'desktop-app-frame'}>
      {!webMode && <DesktopWindowBar title={appTitle} english={locale !== 'zh-CN'} />}
    <div className="app-container" ref={shellRef}>
      <div className="xingshu-aurora-stage" aria-hidden="true">
        <i />
        <i />
        <i />
        <i />
        <i />
        <div className="xingshu-stage-grain" />
      </div>
      <Sidebar mobileOpen={mobileNavOpen} onClose={() => setMobileNavOpen(false)} />
      <div
        className={`mobile-sidebar-backdrop ${mobileNavOpen ? 'is-open' : ''}`}
        aria-hidden="true"
        onClick={() => setMobileNavOpen(false)}
      />
      <div className="main-wrapper">
        <header className={`top-bar ${isScrolled ? 'is-scrolled' : ''}`}>
          <div className="top-bar-title">
            <button
              type="button"
              className="mobile-nav-btn"
              aria-label={locale === 'zh-CN' ? '打开导航菜单' : 'Open navigation menu'}
              aria-expanded={mobileNavOpen}
              onClick={() => setMobileNavOpen((prev) => !prev)}
            >
              <Menu size={18} />
            </button>
            <span className="top-bar-app">{appTitle}</span>
            <span className="top-bar-divider" style={{ color: 'var(--text-muted)' }}>/</span>
            <span className="top-bar-subtitle" style={{ color: 'var(--text-secondary)', fontWeight: 400 }}>
              {webMode
                ? (locale === 'zh-CN' ? '网页版 · 回放分析与公开资料查询' : 'Web Edition · Cloud Replay & Lookup')
                : (report?.snapshot.primary_game ? report.snapshot.primary_game.install_path : t('common.scanningGameDir'))}
            </span>
          </div>

          <div className="top-bar-status">
            <LanguageSwitcher />
            <GitHubRepoLink variant="pill" label="GitHub" hideLabelOnMobile />
            <div className={`status-pill ${webMode ? 'healthy' : statusClass}`}>
              <span className="pulse-dot" />
              <span>{webMode ? (locale === 'zh-CN' ? '网页版在线' : 'Web Ready') : statusLabel}</span>
            </div>
            {!webMode && report?.snapshot.running_processes && report.snapshot.running_processes.length > 0 ? (
              <div className="status-pill warning" title={t('common.gameActive')}>
                <AlertTriangle size={13} />
                <span>{t('common.gameActive')} ({report.snapshot.running_processes.length})</span>
              </div>
            ) : !webMode && report ? (
              <div className="status-pill ok" title={t('common.safeToModify')}>
                <ShieldCheck size={13} />
                <span>{t('common.safeToModify')}</span>
              </div>
            ) : null}
          </div>
        </header>

        {!webMode && <UpdatePromptController />}

        <main className="content-area" ref={contentRef}>
          <div className="aurora-route-stage" key={pathname}>
            <Outlet />
          </div>
        </main>
      </div>
    </div>
      {!webMode && <footer className="desktop-status-bar">
        <span><i className={`desktop-status-dot ${statusClass}`} />{report?.snapshot.primary_game
          ? (locale === 'zh-CN' ? '游戏目录已连接' : 'Game directory connected')
          : (locale === 'zh-CN' ? '等待选择游戏目录' : 'Select a game directory')}</span>
        <span>{locale === 'zh-CN' ? '星枢工作台' : 'XingShu Workspace'} · v0.1.6-beta.2</span>
      </footer>}
    </div>
  );
};
