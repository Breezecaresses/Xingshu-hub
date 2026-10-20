import React, { useEffect, useRef, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { api } from '../../shared/api/tauri';
import type { DiagnosticReport } from '../../shared/types';
import { ShieldCheck, AlertTriangle } from 'lucide-react';
import { useTranslation, LanguageSwitcher } from '../../i18n';
import { getCurrentWebviewWindow } from '@tauri-apps/api/webviewWindow';

export const Layout: React.FC = () => {
  const [report, setReport] = useState<DiagnosticReport | null>(null);
  const { locale, t } = useTranslation();
  const contentRef = useRef<HTMLElement>(null);
  const { pathname } = useLocation();

  useEffect(() => {
    contentRef.current?.scrollTo(0, 0);
  }, [pathname]);

  useEffect(() => {
    const refresh = () => api.getDiagnosticReport()
      .then(setReport)
      .catch(() => setReport(null));
    void refresh();
    const timer = setInterval(() => void refresh(), 10000);
    return () => clearInterval(timer);
  }, []);

  const appTitle = locale === 'zh-CN' ? '星枢 Hub' : 'XingShu Hub';

  useEffect(() => {
    if (typeof document !== 'undefined') {
      document.title = appTitle;
    }
    try {
      const appWin = getCurrentWebviewWindow();
      appWin.setTitle(appTitle).catch((err) => {
        console.warn('Failed to set window title via Tauri:', err);
      });
    } catch {
      // Ignored in non-Tauri environment
    }
  }, [appTitle]);

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
    <div className="app-container">
      <Sidebar />
      <div className="main-wrapper">
        <header className="top-bar">
          <div className="top-bar-title">
            <span>{appTitle}</span>
            <span style={{ color: 'var(--text-muted)' }}>/</span>
            <span style={{ color: 'var(--text-secondary)', fontWeight: 400 }}>
              {report?.snapshot.primary_game ? report.snapshot.primary_game.install_path : t('common.scanningGameDir')}
            </span>
          </div>

          <div className="top-bar-status">
            <LanguageSwitcher />
            <div className={`status-pill ${statusClass}`}>
              <span className="pulse-dot" />
              <span>{statusLabel}</span>
            </div>
            {report?.snapshot.running_processes && report.snapshot.running_processes.length > 0 ? (
              <div className="status-pill warning" title={t('common.gameActive')}>
                <AlertTriangle size={13} />
                <span>{t('common.gameActive')} ({report.snapshot.running_processes.length})</span>
              </div>
            ) : report ? (
              <div className="status-pill ok" title={t('common.safeToModify')}>
                <ShieldCheck size={13} />
                <span>{t('common.safeToModify')}</span>
              </div>
            ) : null}
          </div>
        </header>

        <main className="content-area" ref={contentRef}>
          <Outlet />
        </main>
      </div>
    </div>
  );
};
