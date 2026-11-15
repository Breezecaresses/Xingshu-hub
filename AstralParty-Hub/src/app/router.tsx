import type { ReactNode } from 'react';
import { createBrowserRouter, Navigate } from 'react-router-dom';
import { Layout } from './layout/Layout';
import { DashboardPage } from '../pages/dashboard/DashboardPage';
import { ProfilePage } from '../pages/player/ProfilePage';
import { GameAccountCenter } from '../pages/account/GameAccountCenter';
import { CharactersPage } from '../pages/player/CharactersPage';
import { CardsPage } from '../pages/player/CardsPage';
import { ChipsPage, MapsPage } from '../pages/player/EncyclopediaPage';
import { MatchesPage } from '../pages/player/MatchesPage';
import { ReplayAnalyzerPage } from '../pages/player/ReplayAnalyzerPage';
import { isTauriRuntime, routerBasename } from '../shared/runtime';
import { ModsPage } from '../pages/extensions/ModsPage';
import { PackagesPage } from '../pages/packages/PackagesPage';
import { SpectatorPage } from '../pages/spectator/SpectatorPage';
import { BridgePage } from '../pages/bridge/BridgePage';
import { TransactionsPage } from '../pages/maintenance/TransactionsPage';
import { BackupsPage } from '../pages/maintenance/BackupsPage';
import { DiagnosticsPage } from '../pages/diagnostics/DiagnosticsPage';
import { SettingsPage } from '../pages/settings/SettingsPage';

const desktopOnly = (element: ReactNode) =>
  isTauriRuntime() ? element : <Navigate to="/dashboard" replace />;

export const router = createBrowserRouter([
  {
    path: '/',
    element: <Layout />,
    children: [
      {
        index: true,
        element: (
          <Navigate
            to={
              isTauriRuntime() &&
              typeof window !== 'undefined' &&
              (window.location.search.includes('spectator') || window.location.hash.includes('spectator'))
                ? '/spectator'
                : isTauriRuntime() &&
                  typeof window !== 'undefined' &&
                  (window.location.search.includes('diagnostics') || window.location.hash.includes('diagnostics'))
                ? '/diagnostics'
                : '/dashboard'
            }
            replace
          />
        ),
      },
      {
        path: 'dashboard',
        element: <DashboardPage />,
      },
      // Player & Game Data Centers
      {
        path: 'player/profile',
        element: <ProfilePage />,
      },
      {
        path: 'player/accounts',
        element: <GameAccountCenter />,
      },
      {
        path: 'account',
        element: <GameAccountCenter />,
      },
      {
        path: 'account/center',
        element: <GameAccountCenter />,
      },
      {
        path: 'player/characters',
        element: <CharactersPage />,
      },
      {
        path: 'player/cards',
        element: <CardsPage />,
      },
      {
        path: 'player/chips',
        element: <ChipsPage />,
      },
      {
        path: 'player/maps',
        element: <MapsPage />,
      },
      {
        path: 'player/matches',
        element: <MatchesPage />,
      },
      {
        path: 'player/replay',
        element: <ReplayAnalyzerPage />,
      },
      // XingShu Core Integration (desktop-only)
      {
        path: 'runtime',
        element: desktopOnly(<Navigate to="/diagnostics?section=runtime" replace />),
      },
      {
        path: 'spectator',
        element: desktopOnly(<SpectatorPage />),
      },
      {
        path: 'bridge',
        element: desktopOnly(<BridgePage />),
      },
      // Extensions & Packages (desktop-only)
      {
        path: 'extensions/plugins',
        element: desktopOnly(<Navigate to="/diagnostics?section=runtime" replace />),
      },
      {
        path: 'extensions/mods',
        element: <ModsPage />,
      },
      {
        path: 'extensions/packages',
        element: desktopOnly(<PackagesPage />),
      },
      {
        path: 'extensions/import',
        element: desktopOnly(<Navigate to="/extensions/mods" replace />),
      },
      // Maintenance & System (desktop-only)
      {
        path: 'maintenance/transactions',
        element: desktopOnly(<TransactionsPage />),
      },
      {
        path: 'maintenance/backups',
        element: desktopOnly(<BackupsPage />),
      },
      {
        path: 'diagnostics',
        element: desktopOnly(<DiagnosticsPage />),
      },
      {
        path: 'settings',
        element: desktopOnly(<SettingsPage />),
      },
      {
        path: '*',
        element: <Navigate to="/dashboard" replace />,
      },
    ],
  },
], { basename: routerBasename() });
