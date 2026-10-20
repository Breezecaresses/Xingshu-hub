import { createBrowserRouter, Navigate } from 'react-router-dom';
import { Layout } from './layout/Layout';
import { DashboardPage } from '../pages/dashboard/DashboardPage';
import { ProfilePage } from '../pages/player/ProfilePage';
import { GameAccountCenter } from '../pages/account/GameAccountCenter';
import { CharactersPage } from '../pages/player/CharactersPage';
import { CardsPage } from '../pages/player/CardsPage';
import { ChipsPage, MapsPage } from '../pages/player/EncyclopediaPage';
import { MatchesPage } from '../pages/player/MatchesPage';
import { PluginsPage } from '../pages/extensions/PluginsPage';
import { ModsPage } from '../pages/extensions/ModsPage';
import { ImportPackagePage } from '../pages/extensions/ImportPackagePage';
import { PackagesPage } from '../pages/packages/PackagesPage';
import { RuntimePage } from '../pages/runtime/RuntimePage';
import { SpectatorPage } from '../pages/spectator/SpectatorPage';
import { BridgePage } from '../pages/bridge/BridgePage';
import { TransactionsPage } from '../pages/maintenance/TransactionsPage';
import { BackupsPage } from '../pages/maintenance/BackupsPage';
import { DiagnosticsPage } from '../pages/diagnostics/DiagnosticsPage';
import { SettingsPage } from '../pages/settings/SettingsPage';

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
              typeof window !== 'undefined' &&
              (window.location.search.includes('spectator') || window.location.hash.includes('spectator'))
                ? '/spectator'
                : typeof window !== 'undefined' &&
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
      // XingShu Core Integration
      {
        path: 'runtime',
        element: <RuntimePage />,
      },
      {
        path: 'spectator',
        element: <SpectatorPage />,
      },
      {
        path: 'bridge',
        element: <BridgePage />,
      },
      // Extensions & Packages
      {
        path: 'extensions/plugins',
        element: <PluginsPage />,
      },
      {
        path: 'extensions/mods',
        element: <ModsPage />,
      },
      {
        path: 'extensions/packages',
        element: <PackagesPage />,
      },
      {
        path: 'extensions/import',
        element: <ImportPackagePage />,
      },
      // Maintenance & System
      {
        path: 'maintenance/transactions',
        element: <TransactionsPage />,
      },
      {
        path: 'maintenance/backups',
        element: <BackupsPage />,
      },
      {
        path: 'diagnostics',
        element: <DiagnosticsPage />,
      },
      {
        path: 'settings',
        element: <SettingsPage />,
      },
      {
        path: '*',
        element: <Navigate to="/dashboard" replace />,
      },
    ],
  },
]);
